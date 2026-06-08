using Pintc.TestFixtures;

namespace Pintc.Tests;

public class ParserTests
{
    static ModuleDecl? Parse(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        return new Parser(tokens).ParseModule();
    }

    static (ModuleDecl? Module, IReadOnlyList<Diagnostic> Diagnostics) ParseWithDiagnostics(string source)
    {
        var tokens = new Lexer(source).Tokenize();
        var parser = new Parser(tokens);
        return (parser.ParseModule(), parser.Diagnostics);
    }

    // ── Attributes ─────────────────────────────────────────────────────────────

    [Fact]
    public void Attr_no_args()
    {
        var m = Parse("module x { [noreturn] fun f() -> () { } }");
        m.ShouldNotBeNull();
        var attr = m!.Funs[0].Attributes[0];
        attr.Name.ShouldBe("noreturn");
        attr.Args.ShouldBeEmpty();
    }

    [Fact]
    public void Attr_with_string_args()
    {
        const string src = """
            module x {
                [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
                extern fun f() -> ();
            }
            """;
        var m = Parse(src);
        m.ShouldNotBeNull();
        var attr = m!.Externs[0].Attributes[0];
        attr.Name.ShouldBe("dll_import");
        attr.Get("dll").ShouldBe("kernel32.dll");
        attr.Get("entry_point").ShouldBe("ExitProcess");
    }

    // ── Extern fun decl ────────────────────────────────────────────────────────

    [Fact]
    public void ExternFunDecl_parses_name_params_and_return_type()
    {
        var m = Parse("module x { extern fun exit_process(code: u32) -> (); }");
        m.ShouldNotBeNull();
        var ext = m!.Externs[0];
        ext.Name.ShouldBe("exit_process");
        ext.Params.ShouldHaveSingleItem();
        ext.Params[0].Name.ShouldBe("code");
        ext.Params[0].TypeName.ShouldBe("u32");
        ext.ReturnType.ShouldBe("()");
    }

    // ── Fun decl ───────────────────────────────────────────────────────────────

    [Fact]
    public void FunDecl_empty_body_and_no_params()
    {
        var m = Parse("module x { fun main() -> () { } }");
        m.ShouldNotBeNull();
        var fun = m!.Funs[0];
        fun.Name.ShouldBe("main");
        fun.Params.ShouldBeEmpty();
        fun.ReturnType.ShouldBe("()");
        fun.Body.ShouldBeEmpty();
    }

    [Fact]
    public void FunDecl_body_with_call_stmt()
    {
        var m = Parse("module x { fun main() -> () { exit_process(0); } }");
        m.ShouldNotBeNull();
        var stmt = m!.Funs[0].Body[0];
        stmt.Callee.ShouldBe("exit_process");
        stmt.Args.ShouldHaveSingleItem();
        stmt.Args[0].ShouldBeOfType<IntLiteralExpr>().Value.ShouldBe(0L);
    }

    // ── Integer literal forms ──────────────────────────────────────────────────

    [Theory]
    [InlineData("fun f() -> () { g(42); }",    42L)]
    [InlineData("fun f() -> () { g(0x1F); }",  31L)]
    [InlineData("fun f() -> () { g(0b1010); }", 10L)]
    public void IntLit_decimal_hex_binary(string funSrc, long expected)
    {
        var m = Parse($"module x {{ {funSrc} }}");
        m.ShouldNotBeNull();
        m!.Funs[0].Body[0].Args[0].ShouldBeOfType<IntLiteralExpr>().Value.ShouldBe(expected);
    }

    // ── Error cases ────────────────────────────────────────────────────────────

    [Fact]
    public void Missing_module_keyword_produces_diagnostic()
    {
        var (m, diag) = ParseWithDiagnostics("main { }");
        m.ShouldBeNull();
        diag.ShouldNotBeEmpty();
    }

    // ── Full slice 1 round-trip ────────────────────────────────────────────────

    [Fact]
    public void Slice1_source_parses_to_expected_ast()
    {
        var m = Parse(SliceFixtures.Slice1Source);
        m.ShouldNotBeNull();

        m!.Name.ShouldBe("main");

        // Extern
        m.Externs.ShouldHaveSingleItem();
        var ext = m.Externs[0];
        ext.Name.ShouldBe("exit_process");
        ext.Attributes.Count.ShouldBe(2);
        ext.Attributes[0].Name.ShouldBe("dll_import");
        ext.Attributes[0].Get("dll").ShouldBe("kernel32.dll");
        ext.Attributes[0].Get("entry_point").ShouldBe("ExitProcess");
        ext.Attributes[1].Name.ShouldBe("noreturn");
        ext.Params.ShouldHaveSingleItem();
        ext.Params[0].ShouldBe(new Param("code", "u32"));
        ext.ReturnType.ShouldBe("()");

        // Fun
        m.Funs.ShouldHaveSingleItem();
        var fun = m.Funs[0];
        fun.Name.ShouldBe("main");
        fun.Attributes.Count.ShouldBe(2);
        fun.Attributes[0].Name.ShouldBe("win32_entry");
        fun.Attributes[1].Name.ShouldBe("noreturn");
        fun.Params.ShouldBeEmpty();
        fun.ReturnType.ShouldBe("()");
        fun.Body.ShouldHaveSingleItem();
        var call = fun.Body[0];
        call.Callee.ShouldBe("exit_process");
        call.Args.ShouldHaveSingleItem();
        call.Args[0].ShouldBeOfType<IntLiteralExpr>().Value.ShouldBe(0L);
    }
}
