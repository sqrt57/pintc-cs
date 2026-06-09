namespace Pintc;

static class TypeChecker
{
    public static IReadOnlyList<Diagnostic> Check(ModuleDecl module, ResolveResult resolved)
        => Check([module], resolved);

    public static IReadOnlyList<Diagnostic> Check(List<ModuleDecl> modules, ResolveResult resolved)
    {
        var diagnostics = new List<Diagnostic>();

        foreach (var module in modules)
        {
            foreach (var fun in module.Funs)
            {
                foreach (var stmt in fun.Body)
                {
                    if (stmt is not CallStmt call) continue;
                    if (!resolved.Symbols.TryGetValue(call.Callee, out var symbol))
                        continue; // resolver already reported unknown callees

                    var expectedArity = symbol switch
                    {
                        ExternFunSymbol e => e.Decl.Params.Count,
                        LocalFunSymbol  l => l.Decl.Params.Count,
                        _ => throw new InvalidOperationException($"Unknown symbol type: {symbol}")
                    };

                    if (call.Args.Count != expectedArity)
                        diagnostics.Add(new Diagnostic(
                            Severity.Error,
                            SourceSpan.None,
                            $"'{call.Callee}' called with {call.Args.Count} argument(s) but expects {expectedArity}"));
                }
            }
        }

        return diagnostics;
    }
}
