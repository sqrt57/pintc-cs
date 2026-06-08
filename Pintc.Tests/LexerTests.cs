using Pintc;
using Pintc.TestFixtures;

namespace Pintc.Tests;

public class LexerTests
{
    static TokenKind[] Lex(string source) =>
        new Lexer(source).Tokenize().Select(t => t.Kind).ToArray();

    // ── Keywords ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("module",   TokenKind.Module)]
    [InlineData("extern",   TokenKind.Extern)]
    [InlineData("fun",      TokenKind.Fun)]
    [InlineData("u32",      TokenKind.U32)]
    [InlineData("var",      TokenKind.Var)]
    [InlineData("if",       TokenKind.If)]
    [InlineData("else",     TokenKind.Else)]
    [InlineData("while",    TokenKind.While)]
    [InlineData("return",   TokenKind.Return)]
    [InlineData("and",      TokenKind.And)]
    [InlineData("or",       TokenKind.Or)]
    [InlineData("not",      TokenKind.Not)]
    [InlineData("true",     TokenKind.True)]
    [InlineData("false",    TokenKind.False)]
    [InlineData("nil",      TokenKind.Nil)]
    public void Single_keyword_is_recognized(string source, TokenKind expected)
    {
        Lex(source).ShouldBe([expected, TokenKind.Eof]);
    }

    // ── Identifiers ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("main")]
    [InlineData("exit_process")]
    [InlineData("dll_import")]
    [InlineData("noreturn")]
    [InlineData("win32_entry")]
    [InlineData("entry_point")]
    [InlineData("ExitProcess")]
    [InlineData("_")]
    [InlineData("_abc")]
    [InlineData("a1")]
    public void Single_identifier_is_ident(string source)
    {
        Lex(source).ShouldBe([TokenKind.Ident, TokenKind.Eof]);
    }

    [Theory]
    [InlineData("modules")]  // not a keyword
    [InlineData("funky")]
    [InlineData("extern2")]
    [InlineData("u321")]
    public void Identifier_not_confused_with_keyword_prefix(string source)
    {
        Lex(source).ShouldBe([TokenKind.Ident, TokenKind.Eof]);
    }

    // ── Integer literals ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("0")]
    [InlineData("42")]
    [InlineData("1234567890")]
    [InlineData("0xFF")]
    [InlineData("0xDEAD")]
    [InlineData("0XFF")]
    [InlineData("0b1010")]
    [InlineData("0b0")]
    [InlineData("0B1")]
    public void Integer_literal_is_recognized(string source)
    {
        Lex(source).ShouldBe([TokenKind.IntLit, TokenKind.Eof]);
    }

    // ── Float literals ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("1.0")]
    [InlineData("3.14")]
    [InlineData("1.5e3")]
    [InlineData("2.0E-4")]
    [InlineData("0.5E+2")]
    public void Float_literal_is_recognized(string source)
    {
        Lex(source).ShouldBe([TokenKind.FloatLit, TokenKind.Eof]);
    }

    [Fact]
    public void Integer_not_confused_with_float_prefix()
    {
        // "0." followed by a non-digit is NOT a float — "0" is IntLit, "." is Dot
        Lex("0.x").ShouldBe([TokenKind.IntLit, TokenKind.Dot, TokenKind.Ident, TokenKind.Eof]);
    }

    // ── String literals ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"""hello""")]
    [InlineData(@"""kernel32.dll""")]
    [InlineData(@"""ExitProcess""")]
    [InlineData(@"""unicode \u{1F600}""")]
    public void String_literal_is_recognized(string source)
    {
        Lex(source).ShouldBe([TokenKind.StringLit, TokenKind.Eof]);
    }

    [Fact]
    public void String_literal_with_backslash_escapes()
    {
        // Pint source: "escape \n \t \\ \""  (backslash escapes and escaped quote)
        var source = "\"escape \\n \\t \\\\ \\\"\"";
        Lex(source).ShouldBe([TokenKind.StringLit, TokenKind.Eof]);
    }

    [Fact]
    public void String_literal_text_includes_quotes()
    {
        var tokens = new Lexer(@"""hello""").Tokenize();
        tokens[0].Kind.ShouldBe(TokenKind.StringLit);
        tokens[0].Text.ShouldBe(@"""hello""");
    }

    // ── Character literals ────────────────────────────────────────────────────

    [Theory]
    [InlineData("'a'")]
    [InlineData("'Z'")]
    [InlineData("'\\n'")]
    [InlineData("'\\0'")]
    [InlineData("'\\''")]
    public void Char_literal_is_recognized(string source)
    {
        Lex(source).ShouldBe([TokenKind.CharLit, TokenKind.Eof]);
    }

    // ── Punctuation ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("{",  TokenKind.LBrace)]
    [InlineData("}",  TokenKind.RBrace)]
    [InlineData("[",  TokenKind.LBracket)]
    [InlineData("]",  TokenKind.RBracket)]
    [InlineData("(",  TokenKind.LParen)]
    [InlineData(")",  TokenKind.RParen)]
    [InlineData(",",  TokenKind.Comma)]
    [InlineData(":",  TokenKind.Colon)]
    [InlineData(";",  TokenKind.Semicolon)]
    [InlineData("=",  TokenKind.Eq)]
    [InlineData("==", TokenKind.EqEq)]
    [InlineData("!=", TokenKind.BangEq)]
    [InlineData("->", TokenKind.Arrow)]
    [InlineData(".",  TokenKind.Dot)]
    [InlineData("<",  TokenKind.Lt)]
    [InlineData("<=", TokenKind.LtEq)]
    [InlineData("<<", TokenKind.LtLt)]
    [InlineData(">",  TokenKind.Gt)]
    [InlineData(">=", TokenKind.GtEq)]
    [InlineData(">>", TokenKind.GtGt)]
    [InlineData("-",  TokenKind.Minus)]
    [InlineData("+",  TokenKind.Plus)]
    [InlineData("*",  TokenKind.Star)]
    [InlineData("/",  TokenKind.Slash)]
    [InlineData("%",  TokenKind.Percent)]
    [InlineData("&",  TokenKind.Amp)]
    [InlineData("|",  TokenKind.Pipe)]
    [InlineData("~",  TokenKind.Tilde)]
    [InlineData("^",  TokenKind.Hat)]
    [InlineData("@",  TokenKind.At)]
    public void Single_punctuation_is_recognized(string source, TokenKind expected)
    {
        Lex(source).ShouldBe([expected, TokenKind.Eof]);
    }

    // ── Whitespace and comments ───────────────────────────────────────────────

    [Fact]
    public void Skips_whitespace() =>
        Lex("  \t\r\n  42").ShouldBe([TokenKind.IntLit, TokenKind.Eof]);

    [Fact]
    public void Skips_line_comment() =>
        Lex("// this is a comment\n42").ShouldBe([TokenKind.IntLit, TokenKind.Eof]);

    [Fact]
    public void Skips_block_comment() =>
        Lex("/* block comment */ 42").ShouldBe([TokenKind.IntLit, TokenKind.Eof]);

    [Fact]
    public void Skips_block_comment_spanning_lines() =>
        Lex("/* line1\nline2 */ 42").ShouldBe([TokenKind.IntLit, TokenKind.Eof]);

    [Fact]
    public void Empty_input_gives_only_eof() =>
        Lex("").ShouldBe([TokenKind.Eof]);

    // ── Token span and text ───────────────────────────────────────────────────

    [Fact]
    public void Token_text_is_raw_source_substring()
    {
        var tokens = new Lexer("module 42").Tokenize();
        tokens[0].Text.ShouldBe("module");
        tokens[1].Text.ShouldBe("42");
    }

    [Fact]
    public void Token_span_offset_is_correct()
    {
        var tokens = new Lexer("module 42").Tokenize();
        tokens[0].Span.Offset.ShouldBe(0);
        tokens[0].Span.Length.ShouldBe(6);
        tokens[1].Span.Offset.ShouldBe(7); // after "module "
        tokens[1].Span.Length.ShouldBe(2);
    }

    // ── Error tokens ─────────────────────────────────────────────────────────

    [Fact]
    public void Unterminated_string_produces_error_token()
    {
        var lexer = new Lexer("\"oops");
        var tokens = lexer.Tokenize();
        tokens.ShouldContain(t => t.Kind == TokenKind.Error);
        lexer.Diagnostics.Count.ShouldBe(1);
    }

    [Fact]
    public void Unexpected_character_produces_error_token()
    {
        var lexer = new Lexer("42 $ 0");
        var tokens = lexer.Tokenize();
        tokens.ShouldContain(t => t.Kind == TokenKind.Error);
        lexer.Diagnostics.Count.ShouldBe(1);
    }

    // ── Slice 1 target program ────────────────────────────────────────────────

    [Fact]
    public void Slice1_tokenizes_with_no_errors()
    {
        var lexer = new Lexer(SliceFixtures.Slice1Source);
        lexer.Tokenize();
        lexer.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void Slice1_produces_expected_token_sequence()
    {
        Lex(SliceFixtures.Slice1Source).ShouldBe([
            // module main {
            TokenKind.Module, TokenKind.Ident, TokenKind.LBrace,
            // [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            TokenKind.LBracket, TokenKind.Ident, TokenKind.LParen,
            TokenKind.Ident, TokenKind.Eq, TokenKind.StringLit, TokenKind.Comma,
            TokenKind.Ident, TokenKind.Eq, TokenKind.StringLit,
            TokenKind.RParen, TokenKind.RBracket,
            // [noreturn]
            TokenKind.LBracket, TokenKind.Ident, TokenKind.RBracket,
            // extern fun exit_process(code: u32) -> ();
            TokenKind.Extern, TokenKind.Fun, TokenKind.Ident,
            TokenKind.LParen, TokenKind.Ident, TokenKind.Colon, TokenKind.U32, TokenKind.RParen,
            TokenKind.Arrow, TokenKind.LParen, TokenKind.RParen, TokenKind.Semicolon,
            // [win32_entry]
            TokenKind.LBracket, TokenKind.Ident, TokenKind.RBracket,
            // [noreturn]
            TokenKind.LBracket, TokenKind.Ident, TokenKind.RBracket,
            // fun main() -> () {
            TokenKind.Fun, TokenKind.Ident,
            TokenKind.LParen, TokenKind.RParen,
            TokenKind.Arrow, TokenKind.LParen, TokenKind.RParen,
            TokenKind.LBrace,
            // exit_process(0);
            TokenKind.Ident, TokenKind.LParen, TokenKind.IntLit, TokenKind.RParen, TokenKind.Semicolon,
            // } }
            TokenKind.RBrace, TokenKind.RBrace,
            TokenKind.Eof,
        ]);
    }
}
