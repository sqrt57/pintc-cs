namespace Pintc;

record LocalCallRef(int PatchOffset, string ModuleName, string FuncName);

// Per-function emit context: bundles all state shared across emit helpers.
record FunCtx(
    Dictionary<string, ImportSpec>      ImportMap,
    Dictionary<string, uint>            VarVas,
    Dictionary<string, int>             Offsets,       // locals (neg EBP) + params (pos EBP)
    Dictionary<string, string>          Types,
    Dictionary<string, RecordDecl>      RecordMap,
    List<byte>                          Code,
    List<IatRef>                        IatRefs,
    List<ImportSpec>                    Imports,
    string                              ModuleName,
    IReadOnlyDictionary<string, string> AliasMap,
    List<LocalCallRef>                  LocalCallRefs,
    int                                 LocalBytes,
    bool                                NeedsFrame,
    bool                                IsStdcall,     // true for [dll_export] functions (callee cleans up)
    int                                 ParamStackBytes,
    Dictionary<string, Expr>            Consts);

static class Codegen
{
    const uint ImageBase = 0x00400000u;
    const uint DataRva   = 0x00002000u;

    // Single-module overload: kept for backwards compat (CodegenTests, IntegrationTests).
    public static CodeUnit Emit(ModuleDecl module) => Emit([module]);

    public static CodeUnit Emit(List<ModuleDecl> modules, bool isDll = false)
    {
        var allExterns = modules.SelectMany(m => m.Externs).ToList();
        var allVars    = modules.SelectMany(m => m.Vars).ToList();
        var allRecords = modules.SelectMany(m => m.Records).ToList();

        var importMap = BuildImportMap(allExterns);
        var (varVas, dataBytes) = BuildDataSection(allVars);
        var recordMap = BuildRecordMap(allRecords);

        var code          = new List<byte>();
        var iatRefs       = new List<IatRef>();
        var imports       = new List<ImportSpec>();
        var localCallRefs = new List<LocalCallRef>();
        var funOffsets    = new Dictionary<(string, string), int>();

        // Per-module alias map: alias → module name
        var moduleAliases = modules.ToDictionary(
            m => m.Name,
            m => (IReadOnlyDictionary<string, string>)
                  m.Imports.ToDictionary(i => i.Alias, i => i.ModuleName));

        if (isDll)
        {
            // DLL mode: no [win32_entry] required; emit all functions in declaration order
            foreach (var mod in modules)
                foreach (var fun in mod.Funs)
                {
                    funOffsets[(mod.Name, fun.Name)] = code.Count;
                    EmitFun(fun, mod.Name, moduleAliases[mod.Name],
                            importMap, varVas, recordMap, code, iatRefs, imports, localCallRefs);
                }
        }
        else
        {
            // EXE mode: entry function must be at code offset 0 (PE AddressOfEntryPoint = TextRva)
            ModuleDecl? entryModule = null;
            FunDecl?    entryFun    = null;
            foreach (var mod in modules)
            {
                var f = mod.Funs.FirstOrDefault(f => f.Attributes.Any(a => a.Name == "win32_entry"));
                if (f is not null) { entryModule = mod; entryFun = f; break; }
            }
            if (entryFun is null || entryModule is null)
                throw new InvalidOperationException("No [win32_entry] function found");

            funOffsets[(entryModule.Name, entryFun.Name)] = code.Count;
            EmitFun(entryFun, entryModule.Name, moduleAliases[entryModule.Name],
                    importMap, varVas, recordMap, code, iatRefs, imports, localCallRefs);

            foreach (var mod in modules)
                foreach (var fun in mod.Funs)
                {
                    if (fun == entryFun) continue;
                    funOffsets[(mod.Name, fun.Name)] = code.Count;
                    EmitFun(fun, mod.Name, moduleAliases[mod.Name],
                            importMap, varVas, recordMap, code, iatRefs, imports, localCallRefs);
                }
        }

        // Backpatch internal call displacements
        foreach (var lcr in localCallRefs)
        {
            int targetOffset = funOffsets[(lcr.ModuleName, lcr.FuncName)];
            int rel32 = targetOffset - (lcr.PatchOffset + 4);
            code[lcr.PatchOffset]     = (byte) rel32;
            code[lcr.PatchOffset + 1] = (byte)(rel32 >> 8);
            code[lcr.PatchOffset + 2] = (byte)(rel32 >> 16);
            code[lcr.PatchOffset + 3] = (byte)(rel32 >> 24);
        }

        // Collect DLL exports (only in DLL mode)
        var exportedFuns = new List<ExportedFun>();
        if (isDll)
        {
            foreach (var mod in modules)
                foreach (var fun in mod.Funs)
                    if (fun.Attributes.Any(a => a.Name == "dll_export"))
                        exportedFuns.Add(new ExportedFun(fun.Name, funOffsets[(mod.Name, fun.Name)]));
        }

        return new CodeUnit
        {
            Code         = [.. code],
            IatRefs      = iatRefs,
            Imports      = imports,
            Data         = [.. dataBytes],
            ExportedFuns = exportedFuns,
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

    static string? GetExprType(Expr expr, Dictionary<string, string> types) =>
        expr is VarRefExpr v && types.TryGetValue(v.Name, out var t) ? t : null;

    static int ResolveRecordFieldByteOffset(
        string recordTypeName,
        string fieldName,
        Dictionary<string, RecordDecl> recordMap)
    {
        if (!recordMap.TryGetValue(recordTypeName, out var rec))
            throw new InvalidOperationException($"'{recordTypeName}' is not a record type");
        int offset = 0;
        foreach (var f in rec.Fields)
        {
            if (f.Name == fieldName) return offset;
            offset += StackSlotSize(f.TypeName, recordMap);
        }
        throw new InvalidOperationException($"Record '{recordTypeName}' has no field '{fieldName}'");
    }

    static int ResolveFieldByteOffset(
        string varName, List<string> path,
        Dictionary<string, string> types,
        Dictionary<string, RecordDecl> recordMap)
    {
        int offset = 0;
        string currentType = types[varName];
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

    // Same-named vars in sibling for loops share the last-allocated slot (harmless today; breaks if loops overlap).
    static void CollectLocals(
        IEnumerable<Stmt> stmts,
        Dictionary<string, int> offsets,
        Dictionary<string, string> types,
        Dictionary<string, RecordDecl> recordMap,
        ref int localBytes)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is LocalVarDecl lv)
            {
                localBytes       += StackSlotSize(lv.TypeName, recordMap);
                offsets[lv.Name]  = -localBytes;
                types[lv.Name]    = lv.TypeName;
            }
            else if (stmt is IfStmt ifStmt)
            {
                CollectLocals(ifStmt.Then, offsets, types, recordMap, ref localBytes);
                if (ifStmt.Else is not null)
                    CollectLocals(ifStmt.Else, offsets, types, recordMap, ref localBytes);
            }
            else if (stmt is WhileStmt whileStmt)
                CollectLocals(whileStmt.Body, offsets, types, recordMap, ref localBytes);
            else if (stmt is LoopStmt loopStmt)
                CollectLocals(loopStmt.Body, offsets, types, recordMap, ref localBytes);
            else if (stmt is ForStmt forStmt)
            {
                localBytes                   += StackSlotSize(forStmt.VarTypeName, recordMap);
                offsets[forStmt.VarName]      = -localBytes;
                types[forStmt.VarName]        = forStmt.VarTypeName;
                CollectLocals(forStmt.Body, offsets, types, recordMap, ref localBytes);
            }
        }
    }

    static void EmitFun(
        FunDecl fun,
        string moduleName,
        IReadOnlyDictionary<string, string> aliasMap,
        Dictionary<string, ImportSpec> importMap,
        Dictionary<string, uint> varVas,
        Dictionary<string, RecordDecl> recordMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports,
        List<LocalCallRef> localCallRefs)
    {
        bool isEntryPoint    = fun.Attributes.Any(a => a.Name == "win32_entry");
        bool isNoReturn      = fun.Attributes.Any(a => a.Name == "noreturn");
        bool isDllExport     = fun.Attributes.Any(a => a.Name == "dll_export");
        bool isStdcall       = isDllExport;
        int  paramStackBytes = isStdcall ? fun.Params.Count * 4 : 0;

        var offsets    = new Dictionary<string, int>();
        var types      = new Dictionary<string, string>();
        int localBytes = 0;
        CollectLocals(fun.Body, offsets, types, recordMap, ref localBytes);

        // Parameters: positive EBP offsets ([ebp+8] = first param after saved EBP + ret addr)
        for (int i = 0; i < fun.Params.Count; i++)
        {
            offsets[fun.Params[i].Name] = 8 + i * 4;
            types[fun.Params[i].Name]   = fun.Params[i].TypeName;
        }

        bool needsFrame = !isEntryPoint || localBytes > 0;
        if (needsFrame)
        {
            code.AddRange(X86.PushEbp());
            code.AddRange(X86.MovEbpEsp());
            if (localBytes > 0)
                code.AddRange(X86.SubEspImm8((byte)localBytes));
        }

        var ctx = new FunCtx(importMap, varVas, offsets, types, recordMap,
                             code, iatRefs, imports,
                             moduleName, aliasMap, localCallRefs,
                             localBytes, needsFrame, isStdcall, paramStackBytes, []);

        EmitStmts(fun.Body, ctx);

        if (!isNoReturn)
        {
            if (needsFrame)
            {
                if (localBytes > 0) code.AddRange(X86.Leave());
                else code.AddRange(X86.PopEbp());
            }
            code.AddRange(isStdcall && paramStackBytes > 0
                ? X86.RetN((ushort)paramStackBytes)
                : X86.Ret());
        }
    }

    static void EmitStmts(
        IEnumerable<Stmt> stmts,
        FunCtx ctx,
        List<int>? breakPatches = null,
        List<int>? continuePatches = null)
    {
        foreach (var stmt in stmts)
        {
            switch (stmt)
            {
                case LocalConstDecl lc:
                    ctx.Consts[lc.Name] = lc.Init;
                    break;
                case LocalVarDecl lv:
                    EmitLocalVarDecl(lv, ctx);
                    break;
                case AssignStmt assign:
                    EmitAssignStmt(assign, ctx);
                    break;
                case FieldAssignStmt fieldAssign:
                    EmitFieldAssignStmt(fieldAssign, ctx);
                    break;
                case IndexAssignStmt indexAssign:
                    EmitIndexAssignStmt(indexAssign, ctx);
                    break;
                case DerefAssignStmt derefAssign:
                    EmitDerefAssignStmt(derefAssign, ctx);
                    break;
                case ArrowAssignStmt arrowAssign:
                    EmitArrowAssignStmt(arrowAssign, ctx);
                    break;
                case CallStmt call:
                    EmitCallStmt(call, ctx);
                    break;
                case ReturnStmt ret:
                    EmitReturnStmt(ret, ctx);
                    break;
                case IfStmt ifStmt:
                    EmitIfStmt(ifStmt, ctx, breakPatches, continuePatches);
                    break;
                case ForStmt forStmt:
                    EmitForStmt(forStmt, ctx);
                    break;
                case WhileStmt whileStmt:
                    EmitWhileStmt(whileStmt, ctx);
                    break;
                case LoopStmt loopStmt:
                    EmitLoopStmt(loopStmt, ctx);
                    break;
                case BreakStmt:
                    ctx.Code.AddRange(X86.JmpRel32());
                    breakPatches!.Add(ctx.Code.Count - 4);
                    break;
                case ContinueStmt:
                    ctx.Code.AddRange(X86.JmpRel32());
                    continuePatches!.Add(ctx.Code.Count - 4);
                    break;
            }
        }
    }

    static void EmitIfStmt(
        IfStmt ifStmt,
        FunCtx ctx,
        List<int>? breakPatches = null,
        List<int>? continuePatches = null)
    {
        EmitExpr(ifStmt.Condition, ctx);
        ctx.Code.AddRange(X86.PopEax());
        ctx.Code.AddRange(X86.TestEaxEax());

        ctx.Code.AddRange(X86.JzRel32());
        int jzPatch = ctx.Code.Count - 4;

        EmitStmts(ifStmt.Then, ctx, breakPatches, continuePatches);

        if (ifStmt.Else is null)
        {
            X86.Backpatch(ctx.Code, jzPatch, ctx.Code.Count);
        }
        else
        {
            ctx.Code.AddRange(X86.JmpRel32());
            int jmpPatch = ctx.Code.Count - 4;

            X86.Backpatch(ctx.Code, jzPatch, ctx.Code.Count);

            EmitStmts(ifStmt.Else, ctx, breakPatches, continuePatches);

            X86.Backpatch(ctx.Code, jmpPatch, ctx.Code.Count);
        }
    }

    static void EmitAssignStmt(AssignStmt stmt, FunCtx ctx)
    {
        EmitExpr(stmt.Value, ctx);
        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[stmt.Name]));
    }

    static void EmitFieldAssignStmt(FieldAssignStmt stmt, FunCtx ctx)
    {
        EmitExpr(stmt.Value, ctx);
        int fieldOffset = ResolveFieldByteOffset(stmt.VarName, stmt.Path, ctx.Types, ctx.RecordMap);
        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)(ctx.Offsets[stmt.VarName] + fieldOffset)));
    }

    static void EmitIndexAssignStmt(IndexAssignStmt stmt, FunCtx ctx)
    {
        EmitExpr(stmt.Value, ctx);
        EmitExpr(stmt.Idx, ctx);
        ctx.Code.AddRange(X86.PopEcx());
        ctx.Code.AddRange(X86.PopEax());
        ctx.Code.AddRange(X86.MovEbpEcx4Disp8Eax((sbyte)ctx.Offsets[stmt.ArrayName]));
    }

    static void EmitDerefAssignStmt(DerefAssignStmt stmt, FunCtx ctx)
    {
        EmitExpr(stmt.Value, ctx);
        EmitExpr(stmt.Ptr, ctx);
        ctx.Code.AddRange(X86.PopEcx());
        ctx.Code.AddRange(X86.PopEax());
        ctx.Code.AddRange(X86.MovMemEcxEax());
    }

    static void EmitArrowAssignStmt(ArrowAssignStmt stmt, FunCtx ctx)
    {
        var ptrType = GetExprType(stmt.Ptr, ctx.Types)
            ?? throw new InvalidOperationException("Cannot determine pointer type for arrow-assign");
        int fieldOffset = ResolveRecordFieldByteOffset(ptrType[1..], stmt.Field, ctx.RecordMap);
        EmitExpr(stmt.Value, ctx);
        EmitExpr(stmt.Ptr, ctx);
        ctx.Code.AddRange(X86.PopEcx());
        ctx.Code.AddRange(X86.PopEax());
        if (fieldOffset == 0)
            ctx.Code.AddRange(X86.MovMemEcxEax());
        else
            ctx.Code.AddRange(X86.MovMemEcxDisp8Eax((sbyte)fieldOffset));
    }

    static void EmitWhileStmt(WhileStmt whileStmt, FunCtx ctx)
    {
        int whileTop = ctx.Code.Count;

        EmitExpr(whileStmt.Condition, ctx);
        ctx.Code.AddRange(X86.PopEax());
        ctx.Code.AddRange(X86.TestEaxEax());
        ctx.Code.AddRange(X86.JzRel32());
        int jzPatch = ctx.Code.Count - 4;

        var breakPatches    = new List<int>();
        var continuePatches = new List<int>();

        EmitStmts(whileStmt.Body, ctx, breakPatches, continuePatches);

        foreach (var p in continuePatches) X86.Backpatch(ctx.Code, p, whileTop);

        ctx.Code.AddRange(X86.JmpRel32());
        X86.Backpatch(ctx.Code, ctx.Code.Count - 4, whileTop);

        X86.Backpatch(ctx.Code, jzPatch, ctx.Code.Count);
        foreach (var p in breakPatches) X86.Backpatch(ctx.Code, p, ctx.Code.Count);
    }

    static void EmitForStmt(ForStmt forStmt, FunCtx ctx)
    {
        EmitExpr(forStmt.VarInit, ctx);
        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[forStmt.VarName]));

        int loopTop = ctx.Code.Count;
        EmitExpr(forStmt.Condition, ctx);
        ctx.Code.AddRange(X86.PopEax());
        ctx.Code.AddRange(X86.TestEaxEax());
        ctx.Code.AddRange(X86.JzRel32());
        int jzPatch = ctx.Code.Count - 4;

        var breakPatches    = new List<int>();
        var continuePatches = new List<int>();

        EmitStmts(forStmt.Body, ctx, breakPatches, continuePatches);

        int postOffset = ctx.Code.Count;
        foreach (var p in continuePatches) X86.Backpatch(ctx.Code, p, postOffset);

        EmitExpr(forStmt.PostValue, ctx);
        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[forStmt.PostName]));

        ctx.Code.AddRange(X86.JmpRel32());
        X86.Backpatch(ctx.Code, ctx.Code.Count - 4, loopTop);

        X86.Backpatch(ctx.Code, jzPatch, ctx.Code.Count);
        foreach (var p in breakPatches) X86.Backpatch(ctx.Code, p, ctx.Code.Count);
    }

    static void EmitLoopStmt(LoopStmt loopStmt, FunCtx ctx)
    {
        int loopTop = ctx.Code.Count;

        var breakPatches    = new List<int>();
        var continuePatches = new List<int>();

        EmitStmts(loopStmt.Body, ctx, breakPatches, continuePatches);

        foreach (var p in continuePatches) X86.Backpatch(ctx.Code, p, loopTop);

        ctx.Code.AddRange(X86.JmpRel32());
        X86.Backpatch(ctx.Code, ctx.Code.Count - 4, loopTop);

        foreach (var p in breakPatches) X86.Backpatch(ctx.Code, p, ctx.Code.Count);
    }

    static void EmitLocalVarDecl(LocalVarDecl decl, FunCtx ctx)
    {
        if (decl.Init is null) return;
        EmitExpr(decl.Init, ctx);
        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[decl.Name]));
    }

    static void EmitCallStmt(CallStmt stmt, FunCtx ctx)
    {
        if (!ctx.ImportMap.TryGetValue(stmt.Callee, out var importSpec))
            throw new InvalidOperationException($"Unresolved call target '{stmt.Callee}'");

        if (!ctx.Imports.Contains(importSpec))
            ctx.Imports.Add(importSpec);

        // stdcall: push arguments right-to-left; callee cleans up.
        for (int i = stmt.Args.Count - 1; i >= 0; i--)
            EmitExpr(stmt.Args[i], ctx);

        int iatOffset = ctx.Code.Count + X86.CallIndirectMemAddressOffset;
        ctx.Code.AddRange(X86.CallIndirectMem());
        ctx.IatRefs.Add(new IatRef(iatOffset, importSpec));
    }

    static void EmitReturnStmt(ReturnStmt stmt, FunCtx ctx)
    {
        if (stmt.Value is not null)
        {
            EmitExpr(stmt.Value, ctx);
            ctx.Code.AddRange(X86.PopEax());
        }
        if (ctx.NeedsFrame)
        {
            if (ctx.LocalBytes > 0) ctx.Code.AddRange(X86.Leave());
            else ctx.Code.AddRange(X86.PopEbp());
        }
        ctx.Code.AddRange(ctx.IsStdcall && ctx.ParamStackBytes > 0
            ? X86.RetN((ushort)ctx.ParamStackBytes)
            : X86.Ret());
    }

    static void EmitCallExpr(CallExpr callExpr, FunCtx ctx)
    {
        // Push args right-to-left (works for both cdecl and stdcall)
        for (int i = callExpr.Args.Count - 1; i >= 0; i--)
            EmitExpr(callExpr.Args[i], ctx);

        // Unqualified call to a [dll_import] extern: use IAT-indirect call (stdcall, callee cleans up)
        if (callExpr.Qualifier is null && ctx.ImportMap.TryGetValue(callExpr.Name, out var importSpec))
        {
            if (!ctx.Imports.Contains(importSpec))
                ctx.Imports.Add(importSpec);
            int iatOffset = ctx.Code.Count + X86.CallIndirectMemAddressOffset;
            ctx.Code.AddRange(X86.CallIndirectMem());
            ctx.IatRefs.Add(new IatRef(iatOffset, importSpec));
            ctx.Code.AddRange(X86.PushEax());
            return;
        }

        // Local (intra-binary) Pint function: call rel32 with backpatch (cdecl)
        ctx.Code.AddRange(X86.CallRel32());
        int patchOffset = ctx.Code.Count - 4;

        string targetModule;
        if (callExpr.Qualifier is not null)
        {
            if (!ctx.AliasMap.TryGetValue(callExpr.Qualifier, out targetModule!))
                throw new InvalidOperationException($"Unknown import alias '{callExpr.Qualifier}'");
        }
        else
            targetModule = ctx.ModuleName;

        ctx.LocalCallRefs.Add(new LocalCallRef(patchOffset, targetModule, callExpr.Name));

        // cdecl: caller cleans up arguments
        if (callExpr.Args.Count > 0)
            ctx.Code.AddRange(X86.AddEspImm8((byte)(callExpr.Args.Count * 4)));

        // Return value is in EAX; push onto expression stack
        ctx.Code.AddRange(X86.PushEax());
    }

    static void EmitExpr(Expr expr, FunCtx ctx)
    {
        switch (expr)
        {
            case IntLiteralExpr { Value: >= 0 and <= 127 } e:
                ctx.Code.AddRange(X86.PushImm8((byte)e.Value));
                break;
            case IntLiteralExpr e:
                ctx.Code.AddRange(X86.PushImm32((uint)e.Value));
                break;
            case BoolLiteralExpr { Value: true }:
                ctx.Code.AddRange(X86.PushImm8(1));
                break;
            case BoolLiteralExpr { Value: false }:
                ctx.Code.AddRange(X86.PushImm8(0));
                break;
            case VarRefExpr v when ctx.Consts.TryGetValue(v.Name, out var constExpr):
                EmitExpr(constExpr, ctx);
                break;
            case VarRefExpr v when ctx.Offsets.TryGetValue(v.Name, out int off):
                ctx.Code.AddRange(X86.PushEbpDisp8((sbyte)off));
                break;
            case VarRefExpr v when ctx.VarVas.TryGetValue(v.Name, out uint va):
                ctx.Code.AddRange(X86.PushMem32(va));
                break;
            case VarRefExpr v:
                throw new InvalidOperationException($"Undefined variable '{v.Name}'");
            case FieldAccessExpr fa:
                int fieldOffset = ResolveFieldByteOffset(fa.VarName, fa.Path, ctx.Types, ctx.RecordMap);
                ctx.Code.AddRange(X86.PushEbpDisp8((sbyte)(ctx.Offsets[fa.VarName] + fieldOffset)));
                break;
            case IndexExpr ix:
                EmitExpr(ix.Idx, ctx);
                ctx.Code.AddRange(X86.PopEcx());
                ctx.Code.AddRange(X86.MovEaxEbpEcx4Disp8((sbyte)ctx.Offsets[ix.ArrayName]));
                ctx.Code.AddRange(X86.PushEax());
                break;
            case AddressOfExpr ao:
                EmitAddressOfExpr(ao, ctx);
                break;
            case DerefExpr deref:
                EmitExpr(deref.Ptr, ctx);
                ctx.Code.AddRange(X86.PopEax());
                ctx.Code.AddRange(X86.MovEaxMemEax());
                ctx.Code.AddRange(X86.PushEax());
                break;
            case ArrowExpr arrow: {
                var arrowPtrType = GetExprType(arrow.Ptr, ctx.Types)
                    ?? throw new InvalidOperationException("Cannot determine pointer type for arrow expression");
                int arrowFieldOffset = ResolveRecordFieldByteOffset(arrowPtrType[1..], arrow.Field, ctx.RecordMap);
                EmitExpr(arrow.Ptr, ctx);
                ctx.Code.AddRange(X86.PopEax());
                if (arrowFieldOffset == 0)
                    ctx.Code.AddRange(X86.MovEaxMemEax());
                else
                    ctx.Code.AddRange(X86.MovEaxMemEaxDisp8((sbyte)arrowFieldOffset));
                ctx.Code.AddRange(X86.PushEax());
                break;
            }
            case CallExpr callExpr:
                EmitCallExpr(callExpr, ctx);
                break;
            case UnaryExpr u:
                EmitExpr(u.Operand, ctx);
                ctx.Code.AddRange(X86.PopEax());
                switch (u.Op)
                {
                    case UnaryOp.Neg:    ctx.Code.AddRange(X86.NegEax());    break;
                    case UnaryOp.BitNot: ctx.Code.AddRange(X86.NotEax());    break;
                    case UnaryOp.Not:    ctx.Code.AddRange(X86.XorEaxOne()); break;
                }
                ctx.Code.AddRange(X86.PushEax());
                break;
            case BinaryExpr b: {
                string? leftType = (b.Op is BinaryOp.Add or BinaryOp.Sub)
                    ? GetExprType(b.Left, ctx.Types) : null;
                EmitExpr(b.Left, ctx);
                EmitExpr(b.Right, ctx);
                ctx.Code.AddRange(X86.PopEcx()); // right
                ctx.Code.AddRange(X86.PopEax()); // left
                if (leftType is not null && leftType.StartsWith('^'))
                {
                    int stride = StackSlotSize(leftType[1..], ctx.RecordMap);
                    if (stride != 1) ctx.Code.AddRange(X86.ImulEcxImm8((byte)stride));
                    ctx.Code.AddRange(b.Op == BinaryOp.Add ? X86.AddEaxEcx() : X86.SubEaxEcx());
                    ctx.Code.AddRange(X86.PushEax());
                }
                else
                {
                    EmitBinaryOp(b.Op, ctx.Code);
                }
                break;
            }
            default:
                throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}");
        }
    }

    static void EmitAddressOfExpr(AddressOfExpr ao, FunCtx ctx)
    {
        switch (ao.Operand)
        {
            case VarRefExpr v when ctx.Offsets.TryGetValue(v.Name, out int off):
                ctx.Code.AddRange(X86.LeaEaxEbpDisp8((sbyte)off));
                ctx.Code.AddRange(X86.PushEax());
                break;
            case IndexExpr ix:
                EmitExpr(ix.Idx, ctx);
                ctx.Code.AddRange(X86.PopEcx());
                ctx.Code.AddRange(X86.LeaEaxEbpEcx4Disp8((sbyte)ctx.Offsets[ix.ArrayName]));
                ctx.Code.AddRange(X86.PushEax());
                break;
            case FieldAccessExpr fa:
                int faOffset = ResolveFieldByteOffset(fa.VarName, fa.Path, ctx.Types, ctx.RecordMap);
                ctx.Code.AddRange(X86.LeaEaxEbpDisp8((sbyte)(ctx.Offsets[fa.VarName] + faOffset)));
                ctx.Code.AddRange(X86.PushEax());
                break;
            default:
                throw new NotSupportedException($"Address-of not supported for {ao.Operand.GetType().Name}");
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
