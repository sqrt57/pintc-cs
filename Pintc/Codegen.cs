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
        var recordMap = BuildRecordMap(module.Records);

        // Entry point is the first function emitted; must be at the start of .text.
        var entryFun = module.Funs.FirstOrDefault(f => f.Attributes.Any(a => a.Name == "win32_entry"))
            ?? throw new InvalidOperationException("No [win32_entry] function found");

        var code    = new List<byte>();
        var iatRefs = new List<IatRef>();
        var imports = new List<ImportSpec>();

        EmitFun(entryFun, importMap, varVas, recordMap, code, iatRefs, imports);

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

    static Dictionary<string, RecordDecl> BuildRecordMap(List<RecordDecl> records)
    {
        var map = new Dictionary<string, RecordDecl>();
        foreach (var rec in records)
            map[rec.Name] = rec;
        return map;
    }

    // Scalar locals and array elements occupy one dword each.
    // Records occupy the sum of their fields' slot sizes (recursively).
    static int StackSlotSize(string typeName, Dictionary<string, RecordDecl> recordMap)
    {
        if (typeName.StartsWith('['))
        {
            int close = typeName.IndexOf(']');
            int n = int.Parse(typeName[1..close]);
            return n * StackSlotSize(typeName[(close + 1)..], recordMap);
        }
        if (recordMap.TryGetValue(typeName, out var rec))
            return rec.Fields.Sum(f => StackSlotSize(f.TypeName, recordMap));
        return 4;
    }

    static int TypeSize(string typeName) => typeName switch
    {
        "u8"  or "i8"  or "bool" or "byte"          => 1,
        "u16" or "i16"                               => 2,
        "u32" or "i32" or "usize" or "isize"         => 4,
        "u64" or "i64"                               => 8,
        _ => throw new NotSupportedException($"No known size for type '{typeName}'")
    };

    // Returns the byte offset (relative to the record base) of the field at the end of path.
    // Each step walks into the named field, accumulating the sizes of preceding fields.
    static int ResolveFieldByteOffset(
        string varName, List<string> path,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap)
    {
        int offset = 0;
        string currentType = localTypes[varName];
        foreach (var fieldName in path)
        {
            if (!recordMap.TryGetValue(currentType, out var rec))
                throw new InvalidOperationException($"'{currentType}' is not a record type");
            string? nextType = null;
            foreach (var f in rec.Fields)
            {
                if (f.Name == fieldName) { nextType = f.TypeName; break; }
                offset += StackSlotSize(f.TypeName, recordMap);
            }
            if (nextType is null)
                throw new InvalidOperationException($"Record '{currentType}' has no field '{fieldName}'");
            currentType = nextType;
        }
        return offset;
    }

    static void EmitFun(
        FunDecl fun,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        bool isEntryPoint = fun.Attributes.Any(a => a.Name == "win32_entry");
        bool isNoReturn   = fun.Attributes.Any(a => a.Name == "noreturn");

        // Allocate stack slots for all local variables in the function (including nested blocks).
        var localOffsets = new Dictionary<string, int>();
        var localTypes   = new Dictionary<string, string>();
        int localBytes   = 0;
        CollectLocals(fun.Body, localOffsets, localTypes, recordMap, ref localBytes);

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

        EmitStmts(fun.Body, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports);

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

    // Same-named vars in sibling for loops share the last-allocated slot (harmless today; breaks if loops overlap).
    static void CollectLocals(
        IEnumerable<Stmt> stmts,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        ref int localBytes)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is LocalVarDecl lv)
            {
                localBytes            += StackSlotSize(lv.TypeName, recordMap);
                localOffsets[lv.Name]  = -localBytes;
                localTypes[lv.Name]    = lv.TypeName;
            }
            else if (stmt is IfStmt ifStmt)
            {
                CollectLocals(ifStmt.Then, localOffsets, localTypes, recordMap, ref localBytes);
                if (ifStmt.Else is not null)
                    CollectLocals(ifStmt.Else, localOffsets, localTypes, recordMap, ref localBytes);
            }
            else if (stmt is WhileStmt whileStmt)
                CollectLocals(whileStmt.Body, localOffsets, localTypes, recordMap, ref localBytes);
            else if (stmt is LoopStmt loopStmt)
                CollectLocals(loopStmt.Body, localOffsets, localTypes, recordMap, ref localBytes);
            else if (stmt is ForStmt forStmt)
            {
                localBytes                   += StackSlotSize(forStmt.VarTypeName, recordMap);
                localOffsets[forStmt.VarName] = -localBytes;
                localTypes[forStmt.VarName]   = forStmt.VarTypeName;
                CollectLocals(forStmt.Body, localOffsets, localTypes, recordMap, ref localBytes);
            }
        }
    }

    static void EmitStmts(
        IEnumerable<Stmt> stmts,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports,
        List<int>? breakPatches = null,
        List<int>? continuePatches = null)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case LocalVarDecl lv:
                    EmitLocalVarDecl(lv, varVas, localOffsets, localTypes, recordMap, code);
                    break;
                case AssignStmt assign:
                    EmitAssignStmt(assign, varVas, localOffsets, localTypes, recordMap, code);
                    break;
                case FieldAssignStmt fieldAssign:
                    EmitFieldAssignStmt(fieldAssign, varVas, localOffsets, localTypes, recordMap, code);
                    break;
                case IndexAssignStmt indexAssign:
                    EmitIndexAssignStmt(indexAssign, varVas, localOffsets, localTypes, recordMap, code);
                    break;
                case CallStmt call:
                    EmitCallStmt(call, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports);
                    break;
                case IfStmt ifStmt:
                    EmitIfStmt(ifStmt, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports, breakPatches, continuePatches);
                    break;
                case ForStmt forStmt:
                    EmitForStmt(forStmt, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports);
                    break;
                case WhileStmt whileStmt:
                    EmitWhileStmt(whileStmt, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports);
                    break;
                case LoopStmt loopStmt:
                    EmitLoopStmt(loopStmt, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports);
                    break;
                case BreakStmt:
                    code.AddRange(X86.JmpRel32());
                    breakPatches!.Add(code.Count - 4);
                    break;
                case ContinueStmt:
                    code.AddRange(X86.JmpRel32());
                    continuePatches!.Add(code.Count - 4);
                    break;
            }
        }
    }

    static void EmitIfStmt(
        IfStmt ifStmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports,
        List<int>? breakPatches = null,
        List<int>? continuePatches = null)
    {
        EmitExpr(ifStmt.Condition, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopEax());
        code.AddRange(X86.TestEaxEax());

        code.AddRange(X86.JzRel32());
        int jzPatch = code.Count - 4;

        EmitStmts(ifStmt.Then, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports, breakPatches, continuePatches);

        if (ifStmt.Else is null)
        {
            X86.Backpatch(code, jzPatch, code.Count);
        }
        else
        {
            code.AddRange(X86.JmpRel32());
            int jmpPatch = code.Count - 4;

            X86.Backpatch(code, jzPatch, code.Count);

            EmitStmts(ifStmt.Else, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports, breakPatches, continuePatches);

            X86.Backpatch(code, jmpPatch, code.Count);
        }
    }

    static void EmitAssignStmt(
        AssignStmt stmt,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code)
    {
        EmitExpr(stmt.Value, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopToEbpDisp8((sbyte)localOffsets[stmt.Name]));
    }

    static void EmitFieldAssignStmt(
        FieldAssignStmt stmt,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code)
    {
        EmitExpr(stmt.Value, varVas, localOffsets, localTypes, recordMap, code);
        int fieldOffset = ResolveFieldByteOffset(stmt.VarName, stmt.Path, localTypes, recordMap);
        code.AddRange(X86.PopToEbpDisp8((sbyte)(localOffsets[stmt.VarName] + fieldOffset)));
    }

    static void EmitIndexAssignStmt(
        IndexAssignStmt stmt,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code)
    {
        EmitExpr(stmt.Value, varVas, localOffsets, localTypes, recordMap, code); // push value
        EmitExpr(stmt.Idx,   varVas, localOffsets, localTypes, recordMap, code); // push index
        code.AddRange(X86.PopEcx());                                               // ECX = index
        code.AddRange(X86.PopEax());                                               // EAX = value
        code.AddRange(X86.MovEbpEcx4Disp8Eax((sbyte)localOffsets[stmt.ArrayName]));
    }

    static void EmitWhileStmt(
        WhileStmt whileStmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        int whileTop = code.Count;

        EmitExpr(whileStmt.Condition, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopEax());
        code.AddRange(X86.TestEaxEax());
        code.AddRange(X86.JzRel32());
        int jzPatch = code.Count - 4;

        var breakPatches    = new List<int>();
        var continuePatches = new List<int>();

        EmitStmts(whileStmt.Body, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports, breakPatches, continuePatches);

        foreach (var p in continuePatches) X86.Backpatch(code, p, whileTop);

        code.AddRange(X86.JmpRel32());
        X86.Backpatch(code, code.Count - 4, whileTop);

        X86.Backpatch(code, jzPatch, code.Count);
        foreach (var p in breakPatches) X86.Backpatch(code, p, code.Count);
    }

    static void EmitForStmt(
        ForStmt forStmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        // Init
        EmitExpr(forStmt.VarInit, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopToEbpDisp8((sbyte)localOffsets[forStmt.VarName]));

        // Condition check (loop top)
        int loopTop = code.Count;
        EmitExpr(forStmt.Condition, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopEax());
        code.AddRange(X86.TestEaxEax());
        code.AddRange(X86.JzRel32());
        int jzPatch = code.Count - 4;

        var breakPatches    = new List<int>();
        var continuePatches = new List<int>();

        // Body
        EmitStmts(forStmt.Body, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports, breakPatches, continuePatches);

        // Post step — continue patches land here so post runs before re-checking the condition
        int postOffset = code.Count;
        foreach (var p in continuePatches) X86.Backpatch(code, p, postOffset);

        EmitExpr(forStmt.PostValue, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopToEbpDisp8((sbyte)localOffsets[forStmt.PostName]));

        // Back edge
        code.AddRange(X86.JmpRel32());
        X86.Backpatch(code, code.Count - 4, loopTop);

        // Exit
        X86.Backpatch(code, jzPatch, code.Count);
        foreach (var p in breakPatches) X86.Backpatch(code, p, code.Count);
    }

    static void EmitLoopStmt(
        LoopStmt loopStmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        int loopTop = code.Count;

        var breakPatches    = new List<int>();
        var continuePatches = new List<int>();

        EmitStmts(loopStmt.Body, importMap, varVas, localOffsets, localTypes, recordMap, code, iatRefs, imports, breakPatches, continuePatches);

        foreach (var p in continuePatches) X86.Backpatch(code, p, loopTop);

        code.AddRange(X86.JmpRel32());
        X86.Backpatch(code, code.Count - 4, loopTop);

        foreach (var p in breakPatches) X86.Backpatch(code, p, code.Count);
    }

    static void EmitLocalVarDecl(
        LocalVarDecl decl,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code)
    {
        if (decl.Init is null) return;
        EmitExpr(decl.Init, varVas, localOffsets, localTypes, recordMap, code);
        code.AddRange(X86.PopToEbpDisp8((sbyte)localOffsets[decl.Name]));
    }

    static void EmitCallStmt(
        CallStmt stmt,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
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
            EmitExpr(stmt.Args[i], varVas, localOffsets, localTypes, recordMap, code);

        int iatOffset = code.Count + X86.CallIndirectMemAddressOffset;
        code.AddRange(X86.CallIndirectMem());
        iatRefs.Add(new IatRef(iatOffset, importSpec));
    }

    static void EmitExpr(
        Expr expr,
        Dictionary<string, uint> varVas,
        Dictionary<string, int> localOffsets,
        Dictionary<string, string> localTypes,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code)
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
            case FieldAccessExpr fa:
                int fieldOffset = ResolveFieldByteOffset(fa.VarName, fa.Path, localTypes, recordMap);
                code.AddRange(X86.PushEbpDisp8((sbyte)(localOffsets[fa.VarName] + fieldOffset)));
                break;
            case IndexExpr ix:
                EmitExpr(ix.Idx, varVas, localOffsets, localTypes, recordMap, code); // push index
                code.AddRange(X86.PopEcx());                                          // ECX = index
                code.AddRange(X86.MovEaxEbpEcx4Disp8((sbyte)localOffsets[ix.ArrayName]));
                code.AddRange(X86.PushEax());
                break;
            case UnaryExpr u:
                EmitExpr(u.Operand, varVas, localOffsets, localTypes, recordMap, code);
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
                EmitExpr(b.Left,  varVas, localOffsets, localTypes, recordMap, code);
                EmitExpr(b.Right, varVas, localOffsets, localTypes, recordMap, code);
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
