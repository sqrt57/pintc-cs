using Pintc.TestFixtures;

namespace Pintc.Tests;

public class TypeCheckerTests
{
    static IReadOnlyList<Diagnostic> Check(string source)
    {
        var tokens  = new Lexer(source).Tokenize();
        var module  = new Parser(tokens).ParseModule()!;
        var resolved = Resolver.Resolve(module);
        return TypeChecker.Check(module, resolved);
    }

    [Fact]
    public void Slice1_type_checks_with_no_diagnostics()
    {
        var diagnostics = Check(SliceFixtures.Slice1Source);
        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Wrong_arity_call_produces_error_diagnostic()
    {
        var diagnostics = Check(SliceFixtures.WrongArityCallSource);
        diagnostics.ShouldHaveSingleItem();
        diagnostics[0].Severity.ShouldBe(Severity.Error);
        diagnostics[0].Message.ShouldContain("exit_process");
        diagnostics[0].Message.ShouldContain("argument");
    }

    [Fact]
    public void Correct_arity_call_to_zero_param_fun_produces_no_diagnostics()
    {
        var diagnostics = Check("module x { fun f() -> () { } fun g() -> () { f(); } }");
        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Unknown_callee_is_skipped_without_double_reporting()
    {
        var diagnostics = Check("module x { fun f() -> () { mystery(); } }");
        diagnostics.ShouldBeEmpty();
    }
}
