namespace Pintc;

static class TypeChecker
{
    public static IReadOnlyList<Diagnostic> Check(ModuleDecl module, ResolveResult resolved)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var fun in module.Funs)
        {
            foreach (var stmt in fun.Body)
            {
                if (!resolved.Symbols.TryGetValue(stmt.Callee, out var symbol))
                    continue; // resolver already reported unknown callees

                var expectedArity = symbol switch
                {
                    ExternFunSymbol e => e.Decl.Params.Count,
                    LocalFunSymbol  l => l.Decl.Params.Count,
                    _ => throw new InvalidOperationException($"Unknown symbol type: {symbol}")
                };

                if (stmt.Args.Count != expectedArity)
                    diagnostics.Add(new Diagnostic(
                        Severity.Error,
                        SourceSpan.None,
                        $"'{stmt.Callee}' called with {stmt.Args.Count} argument(s) but expects {expectedArity}"));
            }
        }

        return diagnostics;
    }
}
