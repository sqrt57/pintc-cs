namespace Pintc;

static class Codegen
{
    public static CodeUnit Emit(ModuleDecl module)
    {
        var importMap = BuildImportMap(module.Externs);

        // Entry point is the first function emitted; must be at the start of .text.
        var entryFun = module.Funs.FirstOrDefault(f => f.Attributes.Any(a => a.Name == "win32_entry"))
            ?? throw new InvalidOperationException("No [win32_entry] function found");

        var code    = new List<byte>();
        var iatRefs = new List<IatRef>();
        var imports = new List<ImportSpec>();

        EmitFun(entryFun, importMap, code, iatRefs, imports);

        return new CodeUnit
        {
            Code    = [.. code],
            IatRefs = iatRefs,
            Imports = imports,
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
                dll.Get("dll")          ?? throw new InvalidOperationException($"[dll_import] on '{ext.Name}' missing 'dll'"),
                dll.Get("entry_point")  ?? throw new InvalidOperationException($"[dll_import] on '{ext.Name}' missing 'entry_point'"));
        }
        return map;
    }

    static void EmitFun(
        FunDecl fun,
        Dictionary<string, ImportSpec> importMap,
        List<byte> code,
        List<IatRef> iatRefs,
        List<ImportSpec> imports)
    {
        bool isEntryPoint = fun.Attributes.Any(a => a.Name == "win32_entry");
        bool isNoReturn   = fun.Attributes.Any(a => a.Name == "noreturn");

        // The Win32 entry point is called directly by the OS loader with no caller frame.
        if (!isEntryPoint)
        {
            code.AddRange(X86.PushEbp());
            code.AddRange(X86.MovEbpEsp());
        }

        foreach (var stmt in fun.Body)
            EmitCallStmt(stmt, importMap, code, iatRefs, imports);

        if (!isNoReturn)
        {
            if (!isEntryPoint)
                code.AddRange(X86.PopEbp());
            code.AddRange(X86.Ret());
        }
    }

    static void EmitCallStmt(
        CallStmt stmt,
        Dictionary<string, ImportSpec> importMap,
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
            code.AddRange(EmitExpr(stmt.Args[i]));

        int iatOffset = code.Count + X86.CallIndirectMemAddressOffset;
        code.AddRange(X86.CallIndirectMem());
        iatRefs.Add(new IatRef(iatOffset, importSpec));
    }

    static byte[] EmitExpr(Expr expr) => expr switch
    {
        // Fits in sign-extended imm8 (0–127): use the 2-byte encoding.
        IntLiteralExpr { Value: >= 0 and <= 127 } e => X86.PushImm8((byte)e.Value),
        IntLiteralExpr e                             => X86.PushImm32((uint)e.Value),
        _ => throw new NotSupportedException($"Unsupported expression type: {expr.GetType().Name}"),
    };
}
