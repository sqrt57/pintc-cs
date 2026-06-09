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
    Token Peek() => _pos + 1 < tokens.Count ? tokens[_pos + 1] : tokens[^1];

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
        var funs    = new List<FunDecl>();
        var vars    = new List<ModuleVarDecl>();

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
            else if (Check(TokenKind.Var))
            {
                var v = ParseModuleVarDecl();
                if (v is null) return null;
                vars.Add(v);
            }
            else
            {
                Error($"expected 'extern', 'fun', or 'var', got '{Current.Text}'");
                return null;
            }
        }

        if (Eat(TokenKind.RBrace) is null) return null;
        return new ModuleDecl(name.Text, externs, funs, vars);
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
        var body = ParseBlock();
        if (body is null) return null;
        return new FunDecl(attrs, name.Text, parms, ret, body);
    }

    ModuleVarDecl? ParseModuleVarDecl()
    {
        if (Eat(TokenKind.Var) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Colon) is null) return null;
        var type = ParseType();
        if (type is null) return null;
        Expr? init = null;
        if (TryEat(TokenKind.Eq))
        {
            init = ParseExpr();
            if (init is null) return null;
        }
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ModuleVarDecl(name.Text, type, init);
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

    // Returns "()" for unit type, "[N]T" for array types, or the keyword/ident text for named types.
    string? ParseType()
    {
        if (Check(TokenKind.LParen))
        {
            Advance();
            if (Eat(TokenKind.RParen) is null) return null;
            return "()";
        }

        if (Check(TokenKind.LBracket))
        {
            Advance();
            var sizeTok = Eat(TokenKind.IntLit);
            if (sizeTok is null) return null;
            if (Eat(TokenKind.RBracket) is null) return null;
            var elemType = ParseType();
            if (elemType is null) return null;
            return $"[{sizeTok.Text}]{elemType}";
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

    Stmt? ParseStmt()
    {
        if (Check(TokenKind.Var))
            return ParseLocalVarDecl();
        if (Check(TokenKind.If))
            return ParseIfStmt();
        if (Check(TokenKind.For))
            return ParseForStmt();
        if (Check(TokenKind.While))
            return ParseWhileStmt();
        if (Check(TokenKind.Loop))
            return ParseLoopStmt();
        if (Check(TokenKind.Break))
            return ParseBreakStmt();
        if (Check(TokenKind.Continue))
            return ParseContinueStmt();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Eq)
            return ParseAssignStmt();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.LBracket)
            return ParseIndexAssignStmt();
        return ParseCallStmt();
    }

    List<Stmt>? ParseBlock()
    {
        if (Eat(TokenKind.LBrace) is null) return null;
        var stmts = new List<Stmt>();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var stmt = ParseStmt();
            if (stmt is null) return null;
            stmts.Add(stmt);
        }
        if (Eat(TokenKind.RBrace) is null) return null;
        return stmts;
    }

    IfStmt? ParseIfStmt()
    {
        if (Eat(TokenKind.If) is null) return null;
        if (Eat(TokenKind.LParen) is null) return null;
        var cond = ParseExpr();
        if (cond is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        var then = ParseBlock();
        if (then is null) return null;

        List<Stmt>? elseBranch = null;
        if (TryEat(TokenKind.Else))
        {
            if (Check(TokenKind.If))
            {
                var elseIf = ParseIfStmt();
                if (elseIf is null) return null;
                elseBranch = [elseIf];
            }
            else
            {
                elseBranch = ParseBlock();
                if (elseBranch is null) return null;
            }
        }

        return new IfStmt(cond, then, elseBranch);
    }

    WhileStmt? ParseWhileStmt()
    {
        if (Eat(TokenKind.While) is null) return null;
        if (Eat(TokenKind.LParen) is null) return null;
        var cond = ParseExpr();
        if (cond is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        var body = ParseBlock();
        if (body is null) return null;
        return new WhileStmt(cond, body);
    }

    ForStmt? ParseForStmt()
    {
        if (Eat(TokenKind.For) is null) return null;
        if (Eat(TokenKind.LParen) is null) return null;

        // ForInit: "var" identifier ":" Type "=" Expr
        if (Eat(TokenKind.Var) is null) return null;
        var varName = Eat(TokenKind.Ident);
        if (varName is null) return null;
        if (Eat(TokenKind.Colon) is null) return null;
        var varType = ParseType();
        if (varType is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var varInit = ParseExpr();
        if (varInit is null) return null;

        if (Eat(TokenKind.Semicolon) is null) return null;

        var cond = ParseExpr();
        if (cond is null) return null;

        if (Eat(TokenKind.Semicolon) is null) return null;

        // ForPost: identifier "=" Expr
        var postName = Eat(TokenKind.Ident);
        if (postName is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var postValue = ParseExpr();
        if (postValue is null) return null;

        if (Eat(TokenKind.RParen) is null) return null;

        var body = ParseBlock();
        if (body is null) return null;

        return new ForStmt(varName.Text, varType, varInit, cond, postName.Text, postValue, body);
    }

    LoopStmt? ParseLoopStmt()
    {
        if (Eat(TokenKind.Loop) is null) return null;
        var body = ParseBlock();
        if (body is null) return null;
        return new LoopStmt(body);
    }

    BreakStmt? ParseBreakStmt()
    {
        if (Eat(TokenKind.Break) is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new BreakStmt();
    }

    ContinueStmt? ParseContinueStmt()
    {
        if (Eat(TokenKind.Continue) is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ContinueStmt();
    }

    AssignStmt? ParseAssignStmt()
    {
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new AssignStmt(name.Text, value);
    }

    IndexAssignStmt? ParseIndexAssignStmt()
    {
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.LBracket) is null) return null;
        var idx = ParseExpr();
        if (idx is null) return null;
        if (Eat(TokenKind.RBracket) is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new IndexAssignStmt(name.Text, idx, value);
    }

    LocalVarDecl? ParseLocalVarDecl()
    {
        if (Eat(TokenKind.Var) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Colon) is null) return null;
        var type = ParseType();
        if (type is null) return null;
        Expr? init = null;
        if (TryEat(TokenKind.Eq))
        {
            init = ParseExpr();
            if (init is null) return null;
        }
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new LocalVarDecl(name.Text, type, init);
    }

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

    Expr? ParseExpr() => ParseOrExpr();

    Expr? ParseOrExpr()
    {
        var left = ParseAndExpr();
        if (left is null) return null;
        while (Check(TokenKind.Or))
        {
            Advance();
            var right = ParseAndExpr();
            if (right is null) return null;
            left = new BinaryExpr(BinaryOp.Or, left, right);
        }
        return left;
    }

    Expr? ParseAndExpr()
    {
        var left = ParseBitwiseOrExpr();
        if (left is null) return null;
        while (Check(TokenKind.And))
        {
            Advance();
            var right = ParseBitwiseOrExpr();
            if (right is null) return null;
            left = new BinaryExpr(BinaryOp.And, left, right);
        }
        return left;
    }

    Expr? ParseBitwiseOrExpr()
    {
        var left = ParseXorExpr();
        if (left is null) return null;
        while (Check(TokenKind.Pipe))
        {
            Advance();
            var right = ParseXorExpr();
            if (right is null) return null;
            left = new BinaryExpr(BinaryOp.BitOr, left, right);
        }
        return left;
    }

    Expr? ParseXorExpr()
    {
        var left = ParseBitwiseAndExpr();
        if (left is null) return null;
        while (Check(TokenKind.Xor))
        {
            Advance();
            var right = ParseBitwiseAndExpr();
            if (right is null) return null;
            left = new BinaryExpr(BinaryOp.BitXor, left, right);
        }
        return left;
    }

    Expr? ParseBitwiseAndExpr()
    {
        var left = ParseEqualityExpr();
        if (left is null) return null;
        while (Check(TokenKind.Amp))
        {
            Advance();
            var right = ParseEqualityExpr();
            if (right is null) return null;
            left = new BinaryExpr(BinaryOp.BitAnd, left, right);
        }
        return left;
    }

    Expr? ParseEqualityExpr()
    {
        var left = ParseCompareExpr();
        if (left is null) return null;
        while (Check(TokenKind.EqEq) || Check(TokenKind.BangEq))
        {
            var op = Current.Kind == TokenKind.EqEq ? BinaryOp.Eq : BinaryOp.Ne;
            Advance();
            var right = ParseCompareExpr();
            if (right is null) return null;
            left = new BinaryExpr(op, left, right);
        }
        return left;
    }

    Expr? ParseCompareExpr()
    {
        var left = ParseShiftExpr();
        if (left is null) return null;
        while (Check(TokenKind.Lt) || Check(TokenKind.LtEq) || Check(TokenKind.Gt) || Check(TokenKind.GtEq))
        {
            var op = Current.Kind switch
            {
                TokenKind.Lt   => BinaryOp.Lt,
                TokenKind.LtEq => BinaryOp.Le,
                TokenKind.Gt   => BinaryOp.Gt,
                _              => BinaryOp.Ge,
            };
            Advance();
            var right = ParseShiftExpr();
            if (right is null) return null;
            left = new BinaryExpr(op, left, right);
        }
        return left;
    }

    Expr? ParseShiftExpr()
    {
        var left = ParseAddExpr();
        if (left is null) return null;
        while (Check(TokenKind.LtLt) || Check(TokenKind.GtGt))
        {
            var op = Current.Kind == TokenKind.LtLt ? BinaryOp.Shl : BinaryOp.Shr;
            Advance();
            var right = ParseAddExpr();
            if (right is null) return null;
            left = new BinaryExpr(op, left, right);
        }
        return left;
    }

    Expr? ParseAddExpr()
    {
        var left = ParseMultExpr();
        if (left is null) return null;
        while (Check(TokenKind.Plus) || Check(TokenKind.Minus))
        {
            var op = Current.Kind == TokenKind.Plus ? BinaryOp.Add : BinaryOp.Sub;
            Advance();
            var right = ParseMultExpr();
            if (right is null) return null;
            left = new BinaryExpr(op, left, right);
        }
        return left;
    }

    Expr? ParseMultExpr()
    {
        var left = ParseUnaryExpr();
        if (left is null) return null;
        while (Check(TokenKind.Star) || Check(TokenKind.Slash) || Check(TokenKind.Percent))
        {
            var op = Current.Kind switch
            {
                TokenKind.Star    => BinaryOp.Mul,
                TokenKind.Slash   => BinaryOp.Div,
                _                 => BinaryOp.Mod,
            };
            Advance();
            var right = ParseUnaryExpr();
            if (right is null) return null;
            left = new BinaryExpr(op, left, right);
        }
        return left;
    }

    Expr? ParseUnaryExpr()
    {
        if (Check(TokenKind.Minus))
        {
            Advance();
            var operand = ParseUnaryExpr();
            return operand is null ? null : new UnaryExpr(UnaryOp.Neg, operand);
        }
        if (Check(TokenKind.Tilde))
        {
            Advance();
            var operand = ParseUnaryExpr();
            return operand is null ? null : new UnaryExpr(UnaryOp.BitNot, operand);
        }
        if (Check(TokenKind.Not))
        {
            Advance();
            var operand = ParseUnaryExpr();
            return operand is null ? null : new UnaryExpr(UnaryOp.Not, operand);
        }
        return ParsePrimaryExpr();
    }

    Expr? ParsePrimaryExpr()
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

        if (Check(TokenKind.True))  { Advance(); return new BoolLiteralExpr(true);  }
        if (Check(TokenKind.False)) { Advance(); return new BoolLiteralExpr(false); }

        if (Check(TokenKind.Ident))
        {
            var identName = Advance().Text;
            if (TryEat(TokenKind.LBracket))
            {
                var idx = ParseExpr();
                if (idx is null) return null;
                if (Eat(TokenKind.RBracket) is null) return null;
                return new IndexExpr(identName, idx);
            }
            return new VarRefExpr(identName);
        }

        if (Check(TokenKind.LParen))
        {
            Advance();
            var inner = ParseExpr();
            if (inner is null) return null;
            if (Eat(TokenKind.RParen) is null) return null;
            return inner;
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
