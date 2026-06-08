using Pintc.TestFixtures;

namespace Pintc.Tests;

public class ResolverTests
{
    static ResolveResult Resolve(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var module = new Parser(tokens).ParseModule()!;
        return Resolver.Resolve(module);
    }

    [Fact]
    public void Slice1_resolves_with_no_diagnostics()
    {
        var result = Resolve(SliceFixtures.Slice1Source);
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Call_to_extern_binds_to_extern_decl()
    {
        var tokens = new Lexer(SliceFixtures.Slice1Source).Tokenize();
        var module = new Parser(tokens).ParseModule()!;
        var result = Resolver.Resolve(module);

        var symbol = result.Symbols["exit_process"].ShouldBeOfType<ExternFunSymbol>();
        symbol.Decl.ShouldBe(module.Externs[0]);
    }

    [Fact]
    public void Unknown_callee_produces_error_diagnostic()
    {
        var result = Resolve("module x { fun f() -> () { mystery(); } }");
        result.Diagnostics.ShouldHaveSingleItem();
        result.Diagnostics[0].Severity.ShouldBe(Severity.Error);
        result.Diagnostics[0].Message.ShouldContain("mystery");
    }

    [Fact]
    public void Multiple_unknown_callees_each_produce_a_diagnostic()
    {
        var result = Resolve("module x { fun f() -> () { a(); b(); } }");
        result.Diagnostics.Count.ShouldBe(2);
    }
}
