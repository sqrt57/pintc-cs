namespace Pintc;

class Lexer(string source, int fileIndex = 0)
{
    readonly string _src  = source;
    readonly int    _file = fileIndex;
    int _pos;

    readonly List<Diagnostic> _diagnostics = [];
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    static readonly Dictionary<string, TokenKind> Keywords = new()
    {
        ["and"]      = TokenKind.And,
        ["bool"]     = TokenKind.Bool,
        ["break"]    = TokenKind.Break,
        ["byte"]     = TokenKind.Byte,
        ["const"]    = TokenKind.Const,
        ["continue"] = TokenKind.Continue,
        ["else"]     = TokenKind.Else,
        ["enum"]     = TokenKind.Enum,
        ["export"]   = TokenKind.Export,
        ["extern"]   = TokenKind.Extern,
        ["false"]    = TokenKind.False,
        ["f32"]      = TokenKind.F32,
        ["f64"]      = TokenKind.F64,
        ["for"]      = TokenKind.For,
        ["fun"]      = TokenKind.Fun,
        ["i8"]       = TokenKind.I8,
        ["i16"]      = TokenKind.I16,
        ["i32"]      = TokenKind.I32,
        ["i64"]      = TokenKind.I64,
        ["if"]       = TokenKind.If,
        ["import"]   = TokenKind.Import,
        ["isize"]    = TokenKind.Isize,
        ["loop"]     = TokenKind.Loop,
        ["module"]   = TokenKind.Module,
        ["nil"]      = TokenKind.Nil,
        ["not"]      = TokenKind.Not,
        ["or"]       = TokenKind.Or,
        ["record"]   = TokenKind.Record,
        ["return"]   = TokenKind.Return,
        ["string"]   = TokenKind.String,
        ["true"]     = TokenKind.True,
        ["type"]     = TokenKind.Type,
        ["usize"]    = TokenKind.Usize,
        ["u8"]       = TokenKind.U8,
        ["u16"]      = TokenKind.U16,
        ["u32"]      = TokenKind.U32,
        ["u64"]      = TokenKind.U64,
        ["var"]      = TokenKind.Var,
        ["while"]    = TokenKind.While,
        ["xor"]      = TokenKind.Xor,
    };

    // Returns all tokens in the source, ending with Eof.
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        Token tok;
        do { tokens.Add(tok = Next()); } while (tok.Kind != TokenKind.Eof);
        return tokens;
    }

    Token Next()
    {
        SkipWhitespaceAndComments();
        if (_pos >= _src.Length) return Make(TokenKind.Eof, _pos);

        int start = _pos;
        char c = _src[_pos];

        if (c == '"')  return ScanStringLit(start);
        if (c == '\'') return ScanCharLit(start);
        if (char.IsAsciiDigit(c)) return ScanNumberLit(start);
        if (char.IsAsciiLetter(c) || c == '_') return ScanIdent(start);

        _pos++; // consume the single character before dispatching
        return c switch
        {
            '{' => Make(TokenKind.LBrace,    start),
            '}' => Make(TokenKind.RBrace,    start),
            '[' => Make(TokenKind.LBracket,  start),
            ']' => Make(TokenKind.RBracket,  start),
            '(' => Make(TokenKind.LParen,    start),
            ')' => Make(TokenKind.RParen,    start),
            ',' => Make(TokenKind.Comma,     start),
            ':' => Make(TokenKind.Colon,     start),
            ';' => Make(TokenKind.Semicolon, start),
            '@' => Make(TokenKind.At,        start),
            '^' => Make(TokenKind.Hat,       start),
            '&' => Make(TokenKind.Amp,       start),
            '|' => Make(TokenKind.Pipe,      start),
            '~' => Make(TokenKind.Tilde,     start),
            '*' => Make(TokenKind.Star,      start),
            '/' => Make(TokenKind.Slash,     start), // plain /; comments consumed in SkipWhitespace
            '%' => Make(TokenKind.Percent,   start),
            '+' => Make(TokenKind.Plus,      start),
            '.' => Make(TokenKind.Dot,       start),
            '=' => TryEat('=') ? Make(TokenKind.EqEq,   start) : Make(TokenKind.Eq,    start),
            '!' => TryEat('=') ? Make(TokenKind.BangEq, start) : MakeError(start, "unexpected '!'"),
            '<' => TryEat('=') ? Make(TokenKind.LtEq,   start)
                               : TryEat('<') ? Make(TokenKind.LtLt, start)
                               : Make(TokenKind.Lt, start),
            '>' => TryEat('=') ? Make(TokenKind.GtEq,   start)
                               : TryEat('>') ? Make(TokenKind.GtGt, start)
                               : Make(TokenKind.Gt, start),
            '-' => TryEat('>') ? Make(TokenKind.Arrow,  start) : Make(TokenKind.Minus, start),
            _   => MakeError(start, $"unexpected character U+{(int)c:X4}"),
        };
    }

    void SkipWhitespaceAndComments()
    {
        while (_pos < _src.Length)
        {
            char c = _src[_pos];
            if (c is ' ' or '\t' or '\r' or '\n') { _pos++; continue; }

            if (c == '/' && _pos + 1 < _src.Length)
            {
                if (_src[_pos + 1] == '/')      // line comment
                {
                    while (_pos < _src.Length && _src[_pos] != '\n') _pos++;
                    continue;
                }
                if (_src[_pos + 1] == '*')      // block comment
                {
                    _pos += 2;
                    while (_pos + 1 < _src.Length && !(_src[_pos] == '*' && _src[_pos + 1] == '/'))
                        _pos++;
                    _pos = (_pos + 1 < _src.Length) ? _pos + 2 : _src.Length; // skip */ or reach end
                    continue;
                }
            }
            break;
        }
    }

    Token ScanIdent(int start)
    {
        while (_pos < _src.Length && (char.IsAsciiLetterOrDigit(_src[_pos]) || _src[_pos] == '_'))
            _pos++;
        var text = _src[start.._pos];
        var kind = Keywords.TryGetValue(text, out var kw) ? kw : TokenKind.Ident;
        return new(kind, new SourceSpan(_file, start, _pos - start), text);
    }

    Token ScanNumberLit(int start)
    {
        // Hex: 0x…
        if (_src[_pos] == '0' && _pos + 1 < _src.Length && _src[_pos + 1] is 'x' or 'X')
        {
            _pos += 2;
            if (_pos >= _src.Length || !char.IsAsciiHexDigit(_src[_pos]))
                return MakeError(start, "hex literal has no digits");
            while (_pos < _src.Length && char.IsAsciiHexDigit(_src[_pos])) _pos++;
            return Make(TokenKind.IntLit, start);
        }

        // Binary: 0b…
        if (_src[_pos] == '0' && _pos + 1 < _src.Length && _src[_pos + 1] is 'b' or 'B')
        {
            _pos += 2;
            if (_pos >= _src.Length || _src[_pos] is not '0' and not '1')
                return MakeError(start, "binary literal has no digits");
            while (_pos < _src.Length && _src[_pos] is '0' or '1') _pos++;
            return Make(TokenKind.IntLit, start);
        }

        // Decimal (and float check)
        while (_pos < _src.Length && char.IsAsciiDigit(_src[_pos])) _pos++;

        if (_pos < _src.Length && _src[_pos] == '.'
            && _pos + 1 < _src.Length && char.IsAsciiDigit(_src[_pos + 1]))
        {
            _pos++; // '.'
            while (_pos < _src.Length && char.IsAsciiDigit(_src[_pos])) _pos++;
            if (_pos < _src.Length && _src[_pos] is 'e' or 'E')
            {
                _pos++;
                if (_pos < _src.Length && _src[_pos] is '+' or '-') _pos++;
                while (_pos < _src.Length && char.IsAsciiDigit(_src[_pos])) _pos++;
            }
            return Make(TokenKind.FloatLit, start);
        }

        return Make(TokenKind.IntLit, start);
    }

    Token ScanStringLit(int start)
    {
        _pos++; // opening "
        while (_pos < _src.Length && _src[_pos] != '"' && _src[_pos] != '\n')
        {
            if (_src[_pos] == '\\')
            {
                _pos++; // backslash
                if (_pos < _src.Length && _src[_pos] == 'u') // \u{...}
                {
                    _pos++;
                    if (_pos < _src.Length && _src[_pos] == '{')
                    {
                        _pos++;
                        while (_pos < _src.Length && _src[_pos] != '}') _pos++;
                        if (_pos < _src.Length) _pos++; // }
                    }
                }
                else if (_pos < _src.Length) _pos++; // any other escape char
            }
            else _pos++;
        }
        if (_pos >= _src.Length || _src[_pos] != '"')
            return MakeError(start, "unterminated string literal");
        _pos++; // closing "
        return Make(TokenKind.StringLit, start);
    }

    Token ScanCharLit(int start)
    {
        _pos++; // opening '
        if (_pos < _src.Length && _src[_pos] == '\\')
        {
            _pos++; // backslash
            if (_pos < _src.Length) _pos++; // escape char
        }
        else if (_pos < _src.Length) _pos++; // char
        if (_pos >= _src.Length || _src[_pos] != '\'')
            return MakeError(start, "unterminated character literal");
        _pos++; // closing '
        return Make(TokenKind.CharLit, start);
    }

    bool TryEat(char c) { if (_pos < _src.Length && _src[_pos] == c) { _pos++; return true; } return false; }

    Token Make(TokenKind kind, int start) =>
        new(kind, new SourceSpan(_file, start, _pos - start), _src[start.._pos]);

    Token MakeError(int start, string message)
    {
        _diagnostics.Add(new Diagnostic(Severity.Error, new SourceSpan(_file, start, _pos - start), message));
        return new Token(TokenKind.Error, new SourceSpan(_file, start, _pos - start), _src[start.._pos]);
    }
}
