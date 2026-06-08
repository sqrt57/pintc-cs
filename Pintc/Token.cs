namespace Pintc;

// Location within a source file: byte offset and length.
// File is an index into the compiler's source-file table.
record SourceSpan(int File, int Offset, int Length)
{
    public static readonly SourceSpan None = new(0, 0, 0);
}

public enum TokenKind
{
    // Literals
    IntLit, FloatLit, StringLit, CharLit,

    // Identifier (not a keyword)
    Ident,

    // Keywords — all 40 reserved words from the grammar
    And, Bool, Break, Byte, Const, Continue, Else, Enum,
    Export, Extern, False, F32, F64, For, Fun,
    I8, I16, I32, I64, If, Import, Isize, Loop, Module,
    Nil, Not, Or, Record, Return, String, True, Type,
    Usize, U8, U16, U32, U64, Var, While, Xor,

    // Punctuation — single-char
    LBrace,    // {
    RBrace,    // }
    LBracket,  // [
    RBracket,  // ]
    LParen,    // (
    RParen,    // )
    Comma,     // ,
    Colon,     // :
    Semicolon, // ;
    At,        // @
    Hat,       // ^ (postfix dereference)
    Amp,       // &
    Pipe,      // |
    Tilde,     // ~
    Star,      // *
    Slash,     // /
    Percent,   // %
    Plus,      // +
    Dot,       // .

    // Punctuation — one or two chars (longer match wins)
    Eq,     // =
    EqEq,   // ==
    BangEq, // !=
    Lt,     // <
    LtEq,   // <=
    LtLt,   // <<
    Gt,     // >
    GtEq,   // >=
    GtGt,   // >>
    Minus,  // -
    Arrow,  // ->

    // Special
    Eof,
    Error, // lexer error; a Diagnostic is added for the same span
}

record Token(TokenKind Kind, SourceSpan Span, string Text);
