namespace Pintc;

record LocalCallRef(int PatchOffset, string ModuleName, string FuncName);
record EnumInfo(string UnderlyingType, Dictionary<string, long> Variants);

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
    Dictionary<string, Expr>            Consts,
    List<byte>                          RdataBytes,
    string                              ReturnType,     // this function's return type string
    List<string?>                       ReturnNames,    // this function's return names (empty = positional)
    Dictionary<string, string>          FunReturnTypes, // funcName → returnType for all Pint funs
    Dictionary<string, List<string?>>   FunReturnNames, // funcName → return names for named-return funs
    Dictionary<MultiVarDecl, int>       RetBufOffsets,  // multi-var decl → buffer start EBP offset
    Dictionary<string, List<Param>>     FunParamLists,  // funcName → params, for named-arg reordering
    Dictionary<string, EnumInfo>        EnumMap);       // enumName → evaluated variant values

static class Codegen
{
    const uint ImageBase = 0x00400000u;
    const uint RdataRva  = 0x00002000u;

    // Single-module overload: kept for backwards compat (CodegenTests, IntegrationTests).
    public static CodeUnit Emit(ModuleDecl module) => Emit([module]);

    static bool IsMultiReturn(string returnType) =>
        returnType.StartsWith('(') && returnType != "()";

    // "(T1,T2)" → ["T1", "T2"]
    static List<string> GetMultiReturnTypes(string returnType) =>
        [.. returnType[1..^1].Split(',')];

    public static CodeUnit Emit(List<ModuleDecl> modules, bool isDll = false)
    {
        var allExterns = modules.SelectMany(m => m.Externs).ToList();
        var allVars    = modules.SelectMany(m => m.Vars).ToList();
        var allRecords = modules.SelectMany(m => m.Records).ToList();
        var allConsts  = modules.SelectMany(m => m.Consts).ToList();
        var allEnums   = modules.SelectMany(m => m.Enums).ToList();

        var importMap    = BuildImportMap(allExterns);
        var recordMap    = BuildRecordMap(allRecords);
        var enumMap      = BuildEnumMap(allEnums);
        var rdataBytes   = new List<byte>();
        var moduleConsts = BuildModuleConstMap(allConsts, rdataBytes);
        bool hasRdata    = rdataBytes.Count > 0;
        uint dataRva     = hasRdata ? RdataRva + 0x1000u : RdataRva;
        var (varVas, dataBytes) = BuildDataSection(allVars, dataRva);

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

        // Flat map of all Pint function return types (for multi-return call-site convention)
        var funReturnTypes = new Dictionary<string, string>();
        var funReturnNames = new Dictionary<string, List<string?>>();
        var funParamLists  = new Dictionary<string, List<Param>>();
        foreach (var mod in modules)
            foreach (var fun in mod.Funs)
            {
                funReturnTypes[fun.Name] = fun.ReturnType;
                funParamLists[fun.Name]  = fun.Params;
                if (fun.ReturnNames is not null)
                    funReturnNames[fun.Name] = fun.ReturnNames;
            }

        if (isDll)
        {
            // DLL mode: no [win32_entry] required; emit all functions in declaration order
            foreach (var mod in modules)
                foreach (var fun in mod.Funs)
                {
                    funOffsets[(mod.Name, fun.Name)] = code.Count;
                    EmitFun(fun, mod.Name, moduleAliases[mod.Name],
                            importMap, varVas, recordMap, enumMap, moduleConsts, funReturnTypes, funReturnNames, funParamLists,
                            code, iatRefs, imports, localCallRefs, rdataBytes);
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
                    importMap, varVas, recordMap, enumMap, moduleConsts, funReturnTypes, funReturnNames, funParamLists,
                    code, iatRefs, imports, localCallRefs, rdataBytes);

            foreach (var mod in modules)
                foreach (var fun in mod.Funs)
                {
                    if (fun == entryFun) continue;
                    funOffsets[(mod.Name, fun.Name)] = code.Count;
                    EmitFun(fun, mod.Name, moduleAliases[mod.Name],
                            importMap, varVas, recordMap, enumMap, moduleConsts, funReturnTypes, funReturnNames, funParamLists,
                            code, iatRefs, imports, localCallRefs, rdataBytes);
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
            ReadOnly     = [.. rdataBytes],
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

    static (Dictionary<string, uint> VarVas, List<byte> Data) BuildDataSection(List<ModuleVarDecl> vars, uint dataRva)
    {
        var varVas = new Dictionary<string, uint>();
        var data   = new List<byte>();
        foreach (var v in vars)
        {
            varVas[v.Name] = ImageBase + dataRva + (uint)data.Count;
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

    static Dictionary<string, EnumInfo> BuildEnumMap(List<EnumDecl> enums)
    {
        var map = new Dictionary<string, EnumInfo>();
        foreach (var e in enums)
        {
            var variants = new Dictionary<string, long>();
            long maxSoFar = -1;
            foreach (var v in e.Variants)
            {
                long val = v.Value is IntLiteralExpr lit
                    ? lit.Value
                    : Math.Max(0, maxSoFar + 1);
                maxSoFar     = Math.Max(maxSoFar, val);
                variants[v.Name] = val;
            }
            map[e.Name] = new EnumInfo(e.UnderlyingType ?? "i32", variants);
        }
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

    static int Stride(string typeName, Dictionary<string, RecordDecl> recordMap) => typeName switch
    {
        "u8" or "i8" or "byte" or "bool" => 1,
        "u16" or "i16" => 2,
        _ => StackSlotSize(typeName, recordMap),
    };

    // Actual byte size of a type: N*ByteSize(elem) for arrays, Stride for scalars/records.
    static int ByteSize(string typeName, Dictionary<string, RecordDecl> recordMap)
    {
        if (typeName.StartsWith('['))
        {
            int close = typeName.IndexOf(']');
            int n = int.Parse(typeName[1..close]);
            return n * ByteSize(typeName[(close + 1)..], recordMap);
        }
        return Stride(typeName, recordMap);
    }

    static string? GetExprType(Expr expr, Dictionary<string, string> types) => expr switch
    {
        VarRefExpr v when types.TryGetValue(v.Name, out var t) => t,
        BinaryExpr { Op: BinaryOp.Add or BinaryOp.Sub } b => GetExprType(b.Left, types),
        _ => null,
    };

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

    static bool IsLiteralExpr(Expr expr) => expr is IntLiteralExpr or BoolLiteralExpr or StringLiteralExpr;

    static Dictionary<string, Expr> BuildModuleConstMap(List<ModuleConstDecl> consts, List<byte> rdataBytes)
    {
        var resolved   = new Dictionary<string, Expr>();
        var constInits = consts.ToDictionary(c => c.Name, c => c.Init);
        foreach (var c in consts)
            ResolveModuleConst(c.Name, constInits, resolved, rdataBytes);
        return resolved;
    }

    static void ResolveModuleConst(
        string name,
        Dictionary<string, Expr> constInits,
        Dictionary<string, Expr> resolved,
        List<byte> rdataBytes)
    {
        if (resolved.ContainsKey(name)) return;
        if (!constInits.TryGetValue(name, out var init))
            throw new InvalidOperationException($"Undefined module const '{name}'");
        resolved[name] = EvalConstExpr(init, constInits, resolved, rdataBytes);
    }

    static Expr EvalConstExpr(
        Expr expr,
        Dictionary<string, Expr> constInits,
        Dictionary<string, Expr> resolved,
        List<byte> rdataBytes)
    {
        switch (expr)
        {
            case IntLiteralExpr or BoolLiteralExpr:
                return expr;
            case StringLiteralExpr s:
            {
                uint offset = (uint)rdataBytes.Count;
                rdataBytes.AddRange(s.Bytes);
                rdataBytes.Add(0);
                return new StringConstExpr(offset, s.Bytes.Length);
            }
            case VarRefExpr v:
                ResolveModuleConst(v.Name, constInits, resolved, rdataBytes);
                return resolved[v.Name];
            case UnaryExpr u:
                return EvalConstUnary(u.Op, EvalConstExpr(u.Operand, constInits, resolved, rdataBytes));
            case BinaryExpr b:
                return EvalConstBinary(b.Op,
                    EvalConstExpr(b.Left,  constInits, resolved, rdataBytes),
                    EvalConstExpr(b.Right, constInits, resolved, rdataBytes));
            default:
                throw new InvalidOperationException(
                    $"Non-constant expression in module const initializer: {expr.GetType().Name}");
        }
    }

    static Expr EvalConstUnary(UnaryOp op, Expr operand) =>
        operand switch
        {
            IntLiteralExpr ie => op switch
            {
                UnaryOp.Neg    => new IntLiteralExpr(-ie.Value),
                UnaryOp.BitNot => new IntLiteralExpr(~ie.Value),
                _ => throw new InvalidOperationException($"Unsupported unary op '{op}' on integer const")
            },
            BoolLiteralExpr be => op switch
            {
                UnaryOp.Not => new BoolLiteralExpr(!be.Value),
                _ => throw new InvalidOperationException($"Unsupported unary op '{op}' on bool const")
            },
            _ => throw new InvalidOperationException("Unexpected operand type in const unary")
        };

    static Expr EvalConstBinary(BinaryOp op, Expr left, Expr right)
    {
        if (left is IntLiteralExpr li && right is IntLiteralExpr ri)
        {
            long l = li.Value, r = ri.Value;
            return op switch
            {
                BinaryOp.Add    => new IntLiteralExpr(l + r),
                BinaryOp.Sub    => new IntLiteralExpr(l - r),
                BinaryOp.Mul    => new IntLiteralExpr(l * r),
                BinaryOp.Div    => new IntLiteralExpr(l / r),
                BinaryOp.Mod    => new IntLiteralExpr(l % r),
                BinaryOp.BitAnd => new IntLiteralExpr(l & r),
                BinaryOp.BitOr  => new IntLiteralExpr(l | r),
                BinaryOp.BitXor => new IntLiteralExpr(l ^ r),
                BinaryOp.Shl    => new IntLiteralExpr(l << (int)r),
                BinaryOp.Shr    => new IntLiteralExpr(l >> (int)r),
                BinaryOp.And    => new IntLiteralExpr(l & r),
                BinaryOp.Or     => new IntLiteralExpr(l | r),
                BinaryOp.Eq     => new BoolLiteralExpr(l == r),
                BinaryOp.Ne     => new BoolLiteralExpr(l != r),
                BinaryOp.Lt     => new BoolLiteralExpr(l <  r),
                BinaryOp.Le     => new BoolLiteralExpr(l <= r),
                BinaryOp.Gt     => new BoolLiteralExpr(l >  r),
                BinaryOp.Ge     => new BoolLiteralExpr(l >= r),
                _ => throw new InvalidOperationException($"Unsupported binary op '{op}' on integer consts")
            };
        }
        if (left is BoolLiteralExpr lb && right is BoolLiteralExpr rb)
        {
            return op switch
            {
                BinaryOp.And => new BoolLiteralExpr(lb.Value && rb.Value),
                BinaryOp.Or  => new BoolLiteralExpr(lb.Value || rb.Value),
                BinaryOp.Eq  => new BoolLiteralExpr(lb.Value == rb.Value),
                BinaryOp.Ne  => new BoolLiteralExpr(lb.Value != rb.Value),
                _ => throw new InvalidOperationException($"Unsupported binary op '{op}' on bool consts")
            };
        }
        throw new InvalidOperationException("Mixed or unsupported types in const binary expression");
    }

    // Same-named vars in sibling for loops share the last-allocated slot (harmless today; breaks if loops overlap).
    static void CollectLocals(
        IEnumerable<Stmt> stmts,
        Dictionary<string, int> offsets,
        Dictionary<string, string> types,
        Dictionary<string, RecordDecl> recordMap,
        ref int localBytes,
        Dictionary<MultiVarDecl, int> retBufOffsets)
    {
        foreach (var stmt in stmts)
        {
            if (stmt is LocalVarDecl lv)
            {
                localBytes       += StackSlotSize(lv.TypeName, recordMap);
                offsets[lv.Name]  = -localBytes;
                types[lv.Name]    = lv.TypeName;
            }
            else if (stmt is LocalConstDecl lc && !IsLiteralExpr(lc.Init))
            {
                localBytes       += StackSlotSize(lc.TypeName, recordMap);
                offsets[lc.Name]  = -localBytes;
                types[lc.Name]    = lc.TypeName;
            }
            else if (stmt is MultiVarDecl mvd)
            {
                // Allocate all M slots (including discards) as a contiguous block.
                // Item 0 is at the lowest address (bufStart); item i is at bufStart + i*4.
                // The hidden pointer passed to the callee = address of bufStart.
                int count = mvd.Items.Count;
                localBytes += count * 4;
                int bufStart = -localBytes;
                for (int i = 0; i < count; i++)
                {
                    var (name, typeName) = mvd.Items[i];
                    if (name is not null)
                    {
                        offsets[name] = bufStart + i * 4;
                        types[name]   = typeName!;
                    }
                }
                retBufOffsets[mvd] = bufStart;
            }
            else if (stmt is IfStmt ifStmt)
            {
                CollectLocals(ifStmt.Then, offsets, types, recordMap, ref localBytes, retBufOffsets);
                if (ifStmt.Else is not null)
                    CollectLocals(ifStmt.Else, offsets, types, recordMap, ref localBytes, retBufOffsets);
            }
            else if (stmt is WhileStmt whileStmt)
                CollectLocals(whileStmt.Body, offsets, types, recordMap, ref localBytes, retBufOffsets);
            else if (stmt is LoopStmt loopStmt)
                CollectLocals(loopStmt.Body, offsets, types, recordMap, ref localBytes, retBufOffsets);
            else if (stmt is ForStmt forStmt)
            {
                localBytes                   += StackSlotSize(forStmt.VarTypeName, recordMap);
                offsets[forStmt.VarName]      = -localBytes;
                types[forStmt.VarName]        = forStmt.VarTypeName;
                CollectLocals(forStmt.Body, offsets, types, recordMap, ref localBytes, retBufOffsets);
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
        Dictionary<string, EnumInfo> enumMap,
        Dictionary<string, Expr> moduleConsts,
        Dictionary<string, string> funReturnTypes,
        Dictionary<string, List<string?>> funReturnNames,
        Dictionary<string, List<Param>> funParamLists,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports,
        List<LocalCallRef> localCallRefs,
        List<byte> rdataBytes)
    {
        bool isMultiReturn   = IsMultiReturn(fun.ReturnType);
        bool isEntryPoint    = fun.Attributes.Any(a => a.Name == "win32_entry");
        bool isNoReturn      = fun.Attributes.Any(a => a.Name == "noreturn");
        bool isDllExport     = fun.Attributes.Any(a => a.Name == "dll_export");
        bool isStdcall       = isDllExport;

        var offsets       = new Dictionary<string, int>();
        var types         = new Dictionary<string, string>();
        int localBytes    = 0;
        var retBufOffsets = new Dictionary<MultiVarDecl, int>();
        CollectLocals(fun.Body, offsets, types, recordMap, ref localBytes, retBufOffsets);

        // Parameters: positive EBP offsets.
        // For multi-return functions the hidden pointer occupies [EBP+8]; user params start at [EBP+12].
        int paramBase = isMultiReturn ? 12 : 8;
        for (int i = 0; i < fun.Params.Count; i++)
        {
            offsets[fun.Params[i].Name] = paramBase + i * 4;
            types[fun.Params[i].Name]   = fun.Params[i].TypeName;
        }

        // stdcall cleanup size includes the hidden pointer slot when applicable.
        int paramStackBytes = isStdcall
            ? ((isMultiReturn ? 1 : 0) + fun.Params.Count) * 4
            : 0;

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
                             localBytes, needsFrame, isStdcall, paramStackBytes,
                             new Dictionary<string, Expr>(moduleConsts), rdataBytes,
                             fun.ReturnType, fun.ReturnNames ?? [],
                             funReturnTypes, funReturnNames, retBufOffsets, funParamLists,
                             enumMap);

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
                    if (lc.Init is StringLiteralExpr ls)
                    {
                        uint rdataOffset = (uint)ctx.RdataBytes.Count;
                        ctx.RdataBytes.AddRange(ls.Bytes);
                        ctx.RdataBytes.Add(0);
                        ctx.Consts[lc.Name] = new StringConstExpr(rdataOffset, ls.Bytes.Length);
                    }
                    else if (IsLiteralExpr(lc.Init))
                        ctx.Consts[lc.Name] = lc.Init;
                    else
                    {
                        EmitExpr(lc.Init, ctx);
                        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[lc.Name]));
                    }
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
                case MultiVarDecl mvd:
                    EmitMultiVarDecl(mvd, ctx);
                    break;
                case MultiAssignStmt mas:
                    EmitMultiAssignStmt(mas, ctx);
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
        if (decl.Init is ArrayLiteralExpr arrLit)
        {
            int close    = decl.TypeName.IndexOf(']');
            string elem  = decl.TypeName[(close + 1)..];
            int stride   = StackSlotSize(elem, ctx.RecordMap);
            int baseOff  = ctx.Offsets[decl.Name];
            for (int i = 0; i < arrLit.Elements.Count; i++)
            {
                EmitExpr(arrLit.Elements[i], ctx);
                ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)(baseOff + i * stride)));
            }
            return;
        }
        if (decl.Init is RecordLiteralExpr recLit)
        {
            EmitRecordLiteralInto(recLit, decl.TypeName, ctx.Offsets[decl.Name], ctx);
            return;
        }
        EmitExpr(decl.Init, ctx);
        ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[decl.Name]));
    }

    static void EmitRecordLiteralInto(RecordLiteralExpr lit, string typeName, int baseOff, FunCtx ctx)
    {
        var rec = ctx.RecordMap[typeName];
        int fieldOff = 0;
        foreach (var declField in rec.Fields)
        {
            var (_, value) = lit.Fields.First(f => f.Field == declField.Name);
            int slotOff = baseOff + fieldOff;
            if (value is RecordLiteralExpr nested)
                EmitRecordLiteralInto(nested, declField.TypeName, slotOff, ctx);
            else
            {
                EmitExpr(value, ctx);
                ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)slotOff));
            }
            fieldOff += StackSlotSize(declField.TypeName, ctx.RecordMap);
        }
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
        // Reorder values to match declared return position when named form is used.
        var values = (stmt.ReturnNames is not null && ctx.ReturnNames.Count > 0)
            ? ReorderReturnValues(stmt.Values, stmt.ReturnNames, ctx.ReturnNames)
            : stmt.Values;

        if (IsMultiReturn(ctx.ReturnType) && values.Count > 0)
        {
            // Write each return value through the hidden pointer at [EBP+8].
            // Reload ECX from [EBP+8] before each write so expression evaluation can't corrupt it.
            for (int i = 0; i < values.Count; i++)
            {
                EmitExpr(values[i], ctx);
                ctx.Code.AddRange(X86.PopEax());
                ctx.Code.AddRange(X86.MovEcxEbpDisp8(8)); // mov ecx, [ebp+8]
                if (i == 0)
                    ctx.Code.AddRange(X86.MovMemEcxEax());
                else
                    ctx.Code.AddRange(X86.MovMemEcxDisp8Eax((sbyte)(i * 4)));
            }
        }
        else if (values.Count == 1)
        {
            EmitExpr(values[0], ctx);
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

    // Caller side for: var (a: T, b: T) = call();
    // Return buffer is pre-allocated as a contiguous block in the caller's frame.
    // Stack protocol:
    //   1. LEA EAX, [EBP + bufStart]   — hidden ptr = address of return buffer in frame
    //   2. PUSH EAX                     — save hidden ptr before arg evaluation clobbers it
    //   3. Push user args R-L
    //   4. MOV EAX, [ESP + N*4]         — reload hidden ptr from its saved slot
    //   5. PUSH EAX                     — push as first "arg" (callee sees it at EBP+8)
    //   6. CALL rel32
    //   7. ADD ESP, (1 + N + 1) * 4    — clean up: hidden ptr + N user args + hidden ptr save
    //   Return values are now in their pre-allocated frame slots — no extra pops needed.
    static void EmitMultiVarDecl(MultiVarDecl stmt, FunCtx ctx)
    {
        int bufStart = ctx.RetBufOffsets[stmt];
        if (stmt.Call is DivmodExpr dm)
        {
            EmitExpr(dm.A, ctx); EmitExpr(dm.B, ctx);
            ctx.Code.AddRange(X86.PopEcx()); ctx.Code.AddRange(X86.PopEax());
            ctx.Code.AddRange(X86.XorEdxEdx()); ctx.Code.AddRange(X86.DivEcx());
            ctx.Code.AddRange(X86.MovEbpDisp8Eax((sbyte)bufStart));
            ctx.Code.AddRange(X86.MovEbpDisp8Edx((sbyte)(bufStart + 4)));
            return;
        }
        if (stmt.Call is MulWideExpr mw)
        {
            EmitExpr(mw.A, ctx); EmitExpr(mw.B, ctx);
            ctx.Code.AddRange(X86.PopEcx()); ctx.Code.AddRange(X86.PopEax());
            ctx.Code.AddRange(X86.MulEcx());
            ctx.Code.AddRange(X86.MovEbpDisp8Eax((sbyte)bufStart));
            ctx.Code.AddRange(X86.MovEbpDisp8Edx((sbyte)(bufStart + 4)));
            return;
        }

        var call        = (CallExpr)stmt.Call;
        var callArgs    = call.ArgNames is not null ? ReorderArgs(call, ctx) : call.Args;
        int numUserArgs = callArgs.Count;

        ctx.Code.AddRange(X86.LeaEaxEbpDisp8((sbyte)bufStart)); // hidden ptr
        ctx.Code.AddRange(X86.PushEax());                        // save it

        for (int i = numUserArgs - 1; i >= 0; i--)              // push user args R-L
            EmitExpr(callArgs[i], ctx);

        ctx.Code.AddRange(X86.MovEaxEspDisp8((byte)(numUserArgs * 4))); // reload hidden ptr
        ctx.Code.AddRange(X86.PushEax());                                // push as callee arg

        string targetModule = ctx.ModuleName;
        if (call.Qualifier is not null &&
            !ctx.AliasMap.TryGetValue(call.Qualifier, out targetModule!))
            throw new InvalidOperationException($"Unknown import alias '{call.Qualifier}'");

        ctx.Code.AddRange(X86.CallRel32());
        ctx.LocalCallRefs.Add(new LocalCallRef(ctx.Code.Count - 4, targetModule, call.Name));

        ctx.Code.AddRange(X86.AddEspImm8((byte)((1 + numUserArgs + 1) * 4)));
    }

    // Caller side for: (lo, hi) = call();  or  (quot: q, rem: r) = call();
    // No pre-allocated buffer — allocate a temporary buffer dynamically on the stack,
    // then pop each return value into its existing local slot.
    static void EmitMultiAssignStmt(MultiAssignStmt stmt, FunCtx ctx)
    {
        // Reorder local-variable names to match declared return order when named form is used.
        var names = stmt.Names;
        if (stmt.ReturnNames is not null && stmt.Call is CallExpr namedCall &&
            ctx.FunReturnNames.TryGetValue(namedCall.Name, out var declaredRetNames))
            names = ReorderAssignNames(stmt.Names, stmt.ReturnNames, declaredRetNames);

        if (stmt.Call is DivmodExpr dm)
        {
            EmitExpr(dm.A, ctx); EmitExpr(dm.B, ctx);
            ctx.Code.AddRange(X86.PopEcx()); ctx.Code.AddRange(X86.PopEax());
            ctx.Code.AddRange(X86.XorEdxEdx()); ctx.Code.AddRange(X86.DivEcx());
            if (stmt.Names[0] is string q) ctx.Code.AddRange(X86.MovEbpDisp8Eax((sbyte)ctx.Offsets[q]));
            if (stmt.Names[1] is string r) ctx.Code.AddRange(X86.MovEbpDisp8Edx((sbyte)ctx.Offsets[r]));
            return;
        }
        if (stmt.Call is MulWideExpr mw)
        {
            EmitExpr(mw.A, ctx); EmitExpr(mw.B, ctx);
            ctx.Code.AddRange(X86.PopEcx()); ctx.Code.AddRange(X86.PopEax());
            ctx.Code.AddRange(X86.MulEcx());
            if (stmt.Names[0] is string lo) ctx.Code.AddRange(X86.MovEbpDisp8Eax((sbyte)ctx.Offsets[lo]));
            if (stmt.Names[1] is string hi) ctx.Code.AddRange(X86.MovEbpDisp8Edx((sbyte)ctx.Offsets[hi]));
            return;
        }

        var call        = (CallExpr)stmt.Call;
        var callArgs    = call.ArgNames is not null ? ReorderArgs(call, ctx) : call.Args;
        int numRetVals  = names.Count;
        int numUserArgs = callArgs.Count;

        ctx.Code.AddRange(X86.SubEspImm8((byte)(numRetVals * 4))); // allocate temp buffer
        ctx.Code.AddRange(X86.LeaEaxEsp());                        // hidden ptr = ESP
        ctx.Code.AddRange(X86.PushEax());                          // save it

        for (int i = numUserArgs - 1; i >= 0; i--)                // push user args R-L
            EmitExpr(callArgs[i], ctx);

        ctx.Code.AddRange(X86.MovEaxEspDisp8((byte)(numUserArgs * 4))); // reload hidden ptr
        ctx.Code.AddRange(X86.PushEax());                                // push as callee arg

        string targetModule = ctx.ModuleName;
        if (call.Qualifier is not null &&
            !ctx.AliasMap.TryGetValue(call.Qualifier, out targetModule!))
            throw new InvalidOperationException($"Unknown import alias '{call.Qualifier}'");

        ctx.Code.AddRange(X86.CallRel32());
        ctx.LocalCallRefs.Add(new LocalCallRef(ctx.Code.Count - 4, targetModule, call.Name));

        ctx.Code.AddRange(X86.AddEspImm8((byte)((1 + numUserArgs) * 4))); // hidden ptr + user args
        ctx.Code.AddRange(X86.AddEspImm8(4));                              // hidden ptr save

        // Return buffer is now on top of the stack; pop into target locals (or skip discards).
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i] is string name)
                ctx.Code.AddRange(X86.PopToEbpDisp8((sbyte)ctx.Offsets[name]));
            else
                ctx.Code.AddRange(X86.AddEspImm8(4));
        }
    }

    // Reorder call args to match declared parameter order when names are provided.
    static List<Expr> ReorderArgs(CallExpr call, FunCtx ctx)
    {
        if (!ctx.FunParamLists.TryGetValue(call.Name, out var paramList))
            return call.Args;
        var ordered = new Expr[paramList.Count];
        for (int i = 0; i < call.Args.Count; i++)
        {
            string? argName = call.ArgNames![i];
            if (argName is null)
                ordered[i] = call.Args[i];
            else
            {
                int idx = paramList.FindIndex(p => p.Name == argName);
                if (idx < 0) throw new InvalidOperationException($"No param '{argName}' on '{call.Name}'");
                ordered[idx] = call.Args[i];
            }
        }
        return [.. ordered];
    }

    // Reorder return values to match declared return order for named return statements.
    static List<Expr> ReorderReturnValues(List<Expr> values, List<string?> givenNames, List<string?> declaredNames)
    {
        var ordered = new Expr[declaredNames.Count];
        for (int i = 0; i < givenNames.Count; i++)
        {
            int idx = declaredNames.IndexOf(givenNames[i]);
            if (idx < 0) throw new InvalidOperationException($"Unknown return name '{givenNames[i]}'");
            ordered[idx] = values[i];
        }
        return [.. ordered];
    }

    // Reorder local-variable names to match declared return order for named assign-unpack.
    static List<string?> ReorderAssignNames(List<string?> localNames, List<string?> givenRetNames, List<string?> declaredRetNames)
    {
        var ordered = new string?[declaredRetNames.Count];
        for (int i = 0; i < givenRetNames.Count; i++)
        {
            int idx = declaredRetNames.IndexOf(givenRetNames[i]);
            if (idx < 0) throw new InvalidOperationException($"Unknown return name '{givenRetNames[i]}'");
            ordered[idx] = localNames[i];
        }
        return [.. ordered];
    }

    static void EmitCallExpr(CallExpr callExpr, FunCtx ctx)
    {
        var args = callExpr.ArgNames is not null ? ReorderArgs(callExpr, ctx) : callExpr.Args;
        // Push args right-to-left (works for both cdecl and stdcall)
        for (int i = args.Count - 1; i >= 0; i--)
            EmitExpr(args[i], ctx);

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
            case CharLiteralExpr ch:
                ctx.Code.AddRange(X86.PushImm8(ch.Value));
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
            case FieldAccessExpr fa when ctx.Consts.TryGetValue(fa.VarName, out var constVal) && constVal is StringConstExpr sc:
                if (fa.Path.Count == 1 && fa.Path[0] == "ptr")
                    ctx.Code.AddRange(X86.PushImm32(ImageBase + RdataRva + sc.RdataOffset));
                else if (fa.Path.Count == 1 && fa.Path[0] == "len")
                    ctx.Code.AddRange(X86.PushImm32((uint)sc.ByteCount));
                else
                    throw new InvalidOperationException($"Unknown string field '{string.Join(".", fa.Path)}' on '{fa.VarName}'");
                break;
            case FieldAccessExpr fa when ctx.EnumMap.TryGetValue(fa.VarName, out var enumInfo):
            {
                if (fa.Path.Count != 1 || !enumInfo.Variants.TryGetValue(fa.Path[0], out long enumVal))
                    throw new InvalidOperationException($"'{fa.VarName}.{string.Join(".", fa.Path)}' is not a valid enum member");
                ctx.Code.AddRange(enumVal is >= 0 and <= 127
                    ? X86.PushImm8((byte)enumVal)
                    : X86.PushImm32((uint)enumVal));
                break;
            }
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
            {
                EmitExpr(deref.Ptr, ctx);
                ctx.Code.AddRange(X86.PopEax());
                var ptrType = GetExprType(deref.Ptr, ctx.Types);
                if (ptrType is not null && ptrType.StartsWith('^') && Stride(ptrType[1..], ctx.RecordMap) == 1)
                {
                    ctx.Code.AddRange(X86.MovAlMemEax());
                    ctx.Code.AddRange(X86.MovzxEaxAl());
                }
                else
                    ctx.Code.AddRange(X86.MovEaxMemEax());
                ctx.Code.AddRange(X86.PushEax());
                break;
            }
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
                    int stride = Stride(leftType[1..], ctx.RecordMap);
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
            case CastExpr cast:
            {
                EmitExpr(cast.Value, ctx);
                ctx.Code.AddRange(X86.PopEax());
                string castTarget = ctx.EnumMap.TryGetValue(cast.TargetType, out var castEnumInfo)
                    ? castEnumInfo.UnderlyingType
                    : cast.TargetType;
                switch (castTarget)
                {
                    case "u8":  ctx.Code.AddRange(X86.AndEaxImm32(0xFF));   break;
                    case "u16": ctx.Code.AddRange(X86.AndEaxImm32(0xFFFF)); break;
                }
                ctx.Code.AddRange(X86.PushEax());
                break;
            }
            case SizeofExpr sizeofExpr:
            {
                int sz = ByteSize(sizeofExpr.TypeName, ctx.RecordMap);
                ctx.Code.AddRange(sz is >= 0 and <= 127 ? X86.PushImm8((byte)sz) : X86.PushImm32((uint)sz));
                break;
            }
            case LengthExpr lengthExpr:
            {
                string arrType = ctx.Types[lengthExpr.ArrayName];
                int n = int.Parse(arrType[1..arrType.IndexOf(']')]);
                ctx.Code.AddRange(n is >= 0 and <= 127 ? X86.PushImm8((byte)n) : X86.PushImm32((uint)n));
                break;
            }
            case ToTypeExpr toType:
            {
                EmitExpr(toType.Value, ctx);
                ctx.Code.AddRange(X86.PopEax());
                switch (toType.TargetType)
                {
                    case "to_u8":  ctx.Code.AddRange(X86.AndEaxImm32(0xFF));   break;
                    case "to_u16": ctx.Code.AddRange(X86.AndEaxImm32(0xFFFF)); break;
                    case "to_i8":  ctx.Code.AddRange(X86.MovsxEaxAl());        break;
                    case "to_i16": ctx.Code.AddRange(X86.MovsxEaxAx());        break;
                }
                ctx.Code.AddRange(X86.PushEax());
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
