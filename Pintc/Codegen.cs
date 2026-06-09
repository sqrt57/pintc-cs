namespace Pintc;

static class Codegen
{
    // .data lives at RVA 0x2000 (when present); codegen bakes VAs into code directly.
    const uint ImageBase = 0x00400000u;
    const uint DataRva   = 0x00002000u;

    public static CodeUnit Emit(ModuleDecl module)
    {
        var importMap = BuildImportMap(module.Externs);
        var (varVas, dataBytes) = BuildDataSection(module.Vars);

        // Entry point is the first function emitted; must be at the start of .text.
        var entryFun = module.Funs.FirstOrDefault(f => f.Attributes.Any(a => a.Name == "win32_entry"))
            ?? throw new InvalidOperationException("No [win32_entry] function found");

        var code    = new List<byte>();
        var iatRefs = new List<IatRef>();
        var imports = new List<ImportSpec>();

        EmitFun(entryFun, importMap, varVas, code, iatRefs, imports);

        return new CodeUnit
        {
            Code    = [.. code],
            IatRefs = iatRefs,
            Imports = imports,
            Data    = [.. dataBytes],
        };
    }

    static Dictionary<string, ImportSpec> BuildImportMap(List<ExternFunDecl> externs)
    {
        var map = new Dictionary<string, ImportSpec>();
        foreach (var ext in externs)
        {
            var dll = ext.Attributes.FirstOrDefault(a => a.Name == "dll_import")
                ?? throw new InvalidOperationException($"Extern '{ext.Name}' missing [dll_import]");
            map[ext.Name] = new ImportSpec(
                dll.Get("dll")         ?? throw new InvalidOperationException($"[dll_import] on '{ext.Name}' missing 'dll'"),
                dll.Get("entry_point") ?? throw new InvalidOperationException($"[dll_import] on '{ext.Name}' missing 'entry_point'"));
        }
        return map;
    }

    static (Dictionary<string, uint> VarVas, List<byte> Data) BuildDataSection(List<ModuleVarDecl> vars)
    {
        var varVas = new Dictionary<string, uint>();
        var data   = new List<byte>();

        foreach (var v in vars)
        {
            varVas[v.Name] = ImageBase + DataRva + (uint)data.Count;
            int  size     = TypeSize(v.TypeName);
            long initVal  = v.Init is IntLiteralExpr e ? e.Value : 0L;
            for (int i = 0; i < size; i++)
                data.Add((byte)(initVal >> (8 * i)));
        }

        return (varVas, data);
    }

    // All local variable stack slots are dword-sized; type size governs .data layout only.
    static int StackSlotSize(string _) => 4;

    static int TypeSize(string typeName) => typeName switch
    {
        "u8"  or "i8"  or "bool" or "byte" => 1,
        "u16" or "i16"                      => 2,
        "u32" or "i32" or "usize" or "isize" => 4,
        "u64" or "i64"                      => 8,
        _ => throw new NotSupportedException($"No known size for type '{typeName}'")
    };

    static void EmitFun(
        FunDecl fun,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        bool isEntryPoint = fun.Attributes.Any(a => a.Name == "win32_entry");
        bool isNoReturn   = fun.Attributes.Any(a => a.Name == "noreturn");

        // Allocate stack slots for all local variables in the function (including nested blocks).
        var localOffsets = new Dictionary<string, int>();
        int localBytes   = 0;
        CollectLocals(fun.Body, localOffsets, ref localBytes);

        // Emit a frame when the function has locals or is a regular (non-entry) function.
        // The Win32 entry point is called directly by the OS with no caller frame, but we
        // still need EBP set up when locals are present for [ebp+disp8] addressing.
        bool needsFrame = !isEntryPoint || localBytes > 0;
        if (needsFrame)
        {
            code.AddRange(X86.PushEbp());
            code.AddRange(X86.MovEbpEsp());
            if (localBytes > 0)
                code.AddRange(X86.SubEspImm8((byte)localBytes));
        }

        EmitStmts(fun.Body, importMap, varVas, localOffsets, code, iatRefs, imports);

        if (!isNoReturn)
        {
            if (needsFrame)
            {
                // leave = mov esp,ebp; pop ebp — restores stack and frame pointer.
                if (localBytes > 0)
                    code.AddRange(X86.Leave());
                else
                    code.AddRange(X86.PopEbp());
            }
            code.AddRange(X86.Ret());
        }
    }

    static void CollectLocals(IEnumerable<Stmt> stmts, Dictionary<string, int> localOffsets, ref int localBytes)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is LocalVarDecl lv)
            {
                localBytes            += StackSlotSize(lv.TypeName);
                localOffsets[lv.Name]  = -localBytes;
            }
            else if (stmt is IfStmt ifStmt)
            {
                CollectLocals(ifStmt.Then, localOffsets, ref localBytes);
                if (ifStmt.Else is not null)
                    CollectLocals(ifStmt.Else, localOffsets, ref localBytes);
            }
        }
    }

    static void EmitStmts(
        IEnumerable<Stmt> stmts,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case LocalVarDecl lv:
                    EmitLocalVarDecl(lv, localOffsets, varVas, code);
                    break;
                case CallStmt call:
                    EmitCallStmt(call, importMap, varVas, localOffsets, code, iatRefs, imports);
                    break;
                case IfStmt ifStmt:
                    EmitIfStmt(ifStmt, importMap, varVas, localOffsets, code, iatRefs, imports);
                    break;
            }
        }
    }

    static void EmitIfStmt(
        IfStmt ifStmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        EmitExpr(ifStmt.Condition, varVas, localOffsets, code);
        code.AddRange(X86.PopEax());
        code.AddRange(X86.TestEaxEax());

        code.AddRange(X86.JzRel32());
        int jzPatch = code.Count - 4; // displacement field to patch

        EmitStmts(ifStmt.Then, importMap, varVas, localOffsets, code, iatRefs, imports);

        if (ifStmt.Else is null)
        {
            X86.Backpatch(code, jzPatch, code.Count);
        }
        else
        {
            code.AddRange(X86.JmpRel32());
            int jmpPatch = code.Count - 4;

            X86.Backpatch(code, jzPatch, code.Count);

            EmitStmts(ifStmt.Else, importMap, varVas, localOffsets, code, iatRefs, imports);

            X86.Backpatch(code, jmpPatch, code.Count);
        }
    }

    static void EmitLocalVarDecl(
        LocalVarDecl decl,
        Dictionary<string, int> localOffsets,
        Dictionary<string, uint> varVas,
        List<byte> code)
    {
        if (decl.Init is null) return;
        EmitExpr(decl.Init, varVas, localOffsets, code);
        code.AddRange(X86.PopToEbpDisp8((sbyte)localOffsets[decl.Name]));
    }

    static void EmitCallStmt(
        CallStmt stmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        if (!importMap.TryGetValue(stmt.Callee, out var importSpec))
            throw new InvalidOperationException($"Unresolved call target '{stmt.Callee}'");

        if (!imports.Contains(importSpec))
            imports.Add(importSpec);

        // stdcall: push arguments right-to-left; callee cleans up.
        for (int i = stmt.Args.Count - 1; i >= 0; i--)
            EmitExpr(stmt.Args[i], varVas, localOffsets, code);

        int iatOffset = code.Count + X86.CallIndirectMemAddressOffset;
        code.AddRange(X86.CallIndirectMem());
        iatRefs.Add(new IatRef(iatOffset, importSpec));
    }

    static void EmitExpr(Expr expr, Dictionary<string, uint> varVas, Dictionary<string, int> localOffsets, List<byte> code)
    {
        switch (expr)
        {
            case IntLiteralExpr { Value: >= 0 and <= 127 } e:
                code.AddRange(X86.PushImm8((byte)e.Value));
                break;
            case IntLiteralExpr e:
                code.AddRange(X86.PushImm32((uint)e.Value));
                break;
            case BoolLiteralExpr { Value: true }:
                code.AddRange(X86.PushImm8(1));
                break;
            case BoolLiteralExpr { Value: false }:
                code.AddRange(X86.PushImm8(0));
                break;
            case VarRefExpr v when localOffsets.TryGetValue(v.Name, out int off):
                code.AddRange(X86.PushEbpDisp8((sbyte)off));
                break;
            case VarRefExpr v when varVas.TryGetValue(v.Name, out uint va):
                code.AddRange(X86.PushMem32(va));
                break;
            case VarRefExpr v:
                throw new InvalidOperationException($"Undefined variable '{v.Name}'");
            case UnaryExpr u:
                EmitExpr(u.Operand, varVas, localOffsets, code);
                code.AddRange(X86.PopEax());
                switch (u.Op)
                {
                    case UnaryOp.Neg:    code.AddRange(X86.NegEax());    break;
                    case UnaryOp.BitNot: code.AddRange(X86.NotEax());    break;
                    case UnaryOp.Not:    code.AddRange(X86.XorEaxOne()); break;
                }
                code.AddRange(X86.PushEax());
                break;
            case BinaryExpr b:
                EmitExpr(b.Left,  varVas, localOffsets, code);
                EmitExpr(b.Right, varVas, localOffsets, code);
                code.AddRange(X86.PopEcx()); // right
                code.AddRange(X86.PopEax()); // left
                EmitBinaryOp(b.Op, code);
                break;
            default:
                throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}");
        }
    }

    static void EmitBinaryOp(BinaryOp op, List<byte> code)
    {
        switch (op)
        {
            case BinaryOp.Add:
                code.AddRange(X86.AddEaxEcx());  code.AddRange(X86.PushEax()); break;
            case BinaryOp.Sub:
                code.AddRange(X86.SubEaxEcx());  code.AddRange(X86.PushEax()); break;
            case BinaryOp.Mul:
                code.AddRange(X86.ImulEaxEcx()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Div:
                code.AddRange(X86.XorEdxEdx()); code.AddRange(X86.DivEcx()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Mod:
                code.AddRange(X86.XorEdxEdx()); code.AddRange(X86.DivEcx()); code.AddRange(X86.PushEdx()); break;
            case BinaryOp.BitAnd:
            case BinaryOp.And:
                code.AddRange(X86.AndEaxEcx()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.BitOr:
            case BinaryOp.Or:
                code.AddRange(X86.OrEaxEcx());  code.AddRange(X86.PushEax()); break;
            case BinaryOp.BitXor:
                code.AddRange(X86.XorEaxEcx()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Shl:
                code.AddRange(X86.ShlEaxCl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Shr:
                code.AddRange(X86.ShrEaxCl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Eq:
                code.AddRange(X86.CmpEaxEcx()); code.AddRange(X86.SeteAl());  code.AddRange(X86.MovzxEaxAl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Ne:
                code.AddRange(X86.CmpEaxEcx()); code.AddRange(X86.SetneAl()); code.AddRange(X86.MovzxEaxAl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Lt:
                code.AddRange(X86.CmpEaxEcx()); code.AddRange(X86.SetbAl());  code.AddRange(X86.MovzxEaxAl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Le:
                code.AddRange(X86.CmpEaxEcx()); code.AddRange(X86.SetbeAl()); code.AddRange(X86.MovzxEaxAl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Gt:
                code.AddRange(X86.CmpEaxEcx()); code.AddRange(X86.SetaAl());  code.AddRange(X86.MovzxEaxAl()); code.AddRange(X86.PushEax()); break;
            case BinaryOp.Ge:
                code.AddRange(X86.CmpEaxEcx()); code.AddRange(X86.SetaeAl()); code.AddRange(X86.MovzxEaxAl()); code.AddRange(X86.PushEax()); break;
        }
    }
}
