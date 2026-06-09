namespace Pintc;

abstract record FunSymbol;
record ExternFunSymbol(ExternFunDecl Decl) : FunSymbol;
record LocalFunSymbol(FunDecl Decl) : FunSymbol;

record ResolveResult(
    IReadOnlyDictionary<string, FunSymbol> Symbols,
    IReadOnlyList<Diagnostic> Diagnostics);

static class Resolver
{
    public static ResolveResult Resolve(ModuleDecl module) => Resolve([module]);

    public static ResolveResult Resolve(List<ModuleDecl> modules)
    {
        var symbols = new Dictionary<string, FunSymbol>();
        foreach (var module in modules)
        {
            foreach (var ext in module.Externs)
                symbols[ext.Name] = new ExternFunSymbol(ext);
            foreach (var fun in module.Funs)
                symbols[fun.Name] = new LocalFunSymbol(fun);
        }

        var diagnostics = new List<Diagnostic>();
        foreach (var module in modules)
        {
            foreach (var fun in module.Funs)
            {
                foreach (var stmt in fun.Body)
                {
                    if (stmt is CallStmt call && !symbols.ContainsKey(call.Callee))
                        diagnostics.Add(new Diagnostic(
                            Severity.Error,
                            SourceSpan.None,
                            $"Unknown identifier '{call.Callee}'"));
                }
            }
        }

        return new ResolveResult(symbols, diagnostics);
    }
}
