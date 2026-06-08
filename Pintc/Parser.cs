namespace Pintc;

class Parser(List<Token> tokens)
{
    int _pos;
    readonly List<Diagnostic> _diagnostics = [];
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    Token Current => tokens[_pos];

    Token Advance()
    {
        var t = tokens[_pos];
        if (_pos < tokens.Count - 1) _pos++;
        return t;
    }

    bool Check(TokenKind kind) => Current.Kind == kind;

    Token? Eat(TokenKind kind)
    {
        if (Current.Kind == kind) return Advance();
        _diagnostics.Add(new Diagnostic(Severity.Error, Current.Span,
            $"expected {kind}, got '{Current.Text}'"));
        return null;
    }

    bool TryEat(TokenKind kind)
    {
        if (Current.Kind != kind) return false;
        Advance();
        return true;
    }

    void Error(string message) =>
        _diagnostics.Add(new Diagnostic(Severity.Error, Current.Span, message));

    // ── Top level ──────────────────────────────────────────────────────────────

    public ModuleDecl? ParseModule()
    {
        if (Eat(TokenKind.Module) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.LBrace) is null) return null;

        var externs = new List<ExternFunDecl>();
        var funs = new List<FunDecl>();

        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var attrs = ParseAttrList();
            if (Check(TokenKind.Extern))
            {
                var ext = ParseExternFunDecl(attrs);
                if (ext is null) return null;
                externs.Add(ext);
            }
            else if (Check(TokenKind.Fun))
            {
                var fun = ParseFunDecl(attrs);
                if (fun is null) return null;
                funs.Add(fun);
            }
            else
            {
                Error($"expected 'extern' or 'fun', got '{Current.Text}'");
                return null;
            }
        }

        if (Eat(TokenKind.RBrace) is null) return null;
        return new ModuleDecl(name.Text, externs, funs);
    }

    // ── Attributes ─────────────────────────────────────────────────────────────

    List<Attr> ParseAttrList()
    {
        var attrs = new List<Attr>();
        while (Check(TokenKind.LBracket))
        {
            var attr = ParseAttr();
            if (attr is null) break;
            attrs.Add(attr);
        }
        return attrs;
    }

    Attr? ParseAttr()
    {
        if (Eat(TokenKind.LBracket) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;

        var args = new Dictionary<string, string>();

        if (TryEat(TokenKind.LParen))
        {
            while (!Check(TokenKind.RParen) && !Check(TokenKind.Eof))
            {
                var key = Eat(TokenKind.Ident);
                if (key is null) return null;
                if (Eat(TokenKind.Eq) is null) return null;

                string value;
                if (Check(TokenKind.StringLit))
                {
                    var lit = Advance();
                    value = lit.Text[1..^1]; // strip surrounding quotes
                }
                else if (Check(TokenKind.Ident))
                {
                    value = Advance().Text;
                }
                else
                {
                    Error($"expected string or identifier for attribute value, got '{Current.Text}'");
                    return null;
                }

                args[key.Text] = value;
                if (!TryEat(TokenKind.Comma)) break;
            }
            if (Eat(TokenKind.RParen) is null) return null;
        }

        if (Eat(TokenKind.RBracket) is null) return null;
        return new Attr(name.Text, args);
    }

    // ── Declarations ───────────────────────────────────────────────────────────

    ExternFunDecl? ParseExternFunDecl(List<Attr> attrs)
    {
        if (Eat(TokenKind.Extern) is null) return null;
        if (Eat(TokenKind.Fun) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        var parms = ParseParamList();
        if (parms is null) return null;
        if (Eat(TokenKind.Arrow) is null) return null;
        var ret = ParseType();
        if (ret is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ExternFunDecl(attrs, name.Text, parms, ret);
    }

    FunDecl? ParseFunDecl(List<Attr> attrs)
    {
        if (Eat(TokenKind.Fun) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        var parms = ParseParamList();
        if (parms is null) return null;
        if (Eat(TokenKind.Arrow) is null) return null;
        var ret = ParseType();
        if (ret is null) return null;
        if (Eat(TokenKind.LBrace) is null) return null;

        var body = new List<CallStmt>();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var stmt = ParseCallStmt();
            if (stmt is null) return null;
            body.Add(stmt);
        }

        if (Eat(TokenKind.RBrace) is null) return null;
        return new FunDecl(attrs, name.Text, parms, ret, body);
    }

    // ── Parameters and types ───────────────────────────────────────────────────

    List<Param>? ParseParamList()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var parms = new List<Param>();

        while (!Check(TokenKind.RParen) && !Check(TokenKind.Eof))
        {
            var pname = Eat(TokenKind.Ident);
            if (pname is null) return null;
            if (Eat(TokenKind.Colon) is null) return null;
            var ptype = ParseType();
            if (ptype is null) return null;
            parms.Add(new Param(pname.Text, ptype));
            if (!TryEat(TokenKind.Comma)) break;
        }

        if (Eat(TokenKind.RParen) is null) return null;
        return parms;
    }

    // Returns "()" for unit type, or the keyword/ident text for named types.
    string? ParseType()
    {
        if (Check(TokenKind.LParen))
        {
            Advance();
            if (Eat(TokenKind.RParen) is null) return null;
            return "()";
        }

        if (IsTypeName(Current.Kind))
            return Advance().Text;

        Error($"expected type, got '{Current.Text}'");
        return null;
    }

    static bool IsTypeName(TokenKind kind) => kind is
        TokenKind.Ident  or
        TokenKind.Bool   or TokenKind.Byte  or
        TokenKind.I8     or TokenKind.I16   or TokenKind.I32  or TokenKind.I64  or
        TokenKind.U8     or TokenKind.U16   or TokenKind.U32  or TokenKind.U64  or
        TokenKind.F32    or TokenKind.F64   or
        TokenKind.Isize  or TokenKind.Usize or
        TokenKind.String;

    // ── Statements and expressions ─────────────────────────────────────────────

    CallStmt? ParseCallStmt()
    {
        var callee = Eat(TokenKind.Ident);
        if (callee is null) return null;
        if (Eat(TokenKind.LParen) is null) return null;

        var args = new List<Expr>();
        while (!Check(TokenKind.RParen) && !Check(TokenKind.Eof))
        {
            var arg = ParseExpr();
            if (arg is null) return null;
            args.Add(arg);
            if (!TryEat(TokenKind.Comma)) break;
        }

        if (Eat(TokenKind.RParen) is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new CallStmt(callee.Text, args);
    }

    Expr? ParseExpr()
    {
        if (Check(TokenKind.IntLit))
        {
            var tok = Advance();
            if (!TryParseIntLit(tok.Text, out long value))
            {
                _diagnostics.Add(new Diagnostic(Severity.Error, tok.Span,
                    $"invalid integer literal '{tok.Text}'"));
                return null;
            }
            return new IntLiteralExpr(value);
        }

        Error($"expected expression, got '{Current.Text}'");
        return null;
    }

    static bool TryParseIntLit(string text, out long value)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], System.Globalization.NumberStyles.HexNumber, null, out value);
        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            value = 0;
            foreach (var c in text[2..])
            {
                if (c is not ('0' or '1')) return false;
                value = (value << 1) | (c == '1' ? 1L : 0L);
            }
            return true;
        }
        return long.TryParse(text, out value);
    }
}
