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
    Token PeekAt(int offset) { int idx = _pos + offset; return idx < tokens.Count ? tokens[idx] : tokens[^1]; }

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

    public List<ModuleDecl> ParseProgram()
    {
        var modules = new List<ModuleDecl>();
        while (!Check(TokenKind.Eof))
        {
            var module = ParseModule();
            if (module is null) break;
            modules.Add(module);
        }
        return modules;
    }

    public ModuleDecl? ParseModule()
    {
        if (Eat(TokenKind.Module) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.LBrace) is null) return null;

        var externs = new List<ExternFunDecl>();
        var funs    = new List<FunDecl>();
        var vars    = new List<ModuleVarDecl>();
        var records = new List<RecordDecl>();
        var imports = new List<ImportDecl>();
        var exports = new List<string>();
        var consts  = new List<ModuleConstDecl>();

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
            else if (Check(TokenKind.Record))
            {
                var rec = ParseRecordDecl();
                if (rec is null) return null;
                records.Add(rec);
            }
            else if (Check(TokenKind.Import))
            {
                var imp = ParseImportDecl();
                if (imp is null) return null;
                imports.Add(imp);
            }
            else if (Check(TokenKind.Export))
            {
                var exp = ParseExportDecl();
                if (exp is null) return null;
                exports.Add(exp);
            }
            else if (Check(TokenKind.Const))
            {
                var c = ParseModuleConstDecl();
                if (c is null) return null;
                consts.Add(c);
            }
            else
            {
                Error($"expected 'extern', 'fun', 'record', 'var', 'const', 'import', or 'export', got '{Current.Text}'");
                return null;
            }
        }

        if (Eat(TokenKind.RBrace) is null) return null;
        return new ModuleDecl(name.Text, externs, funs, vars, records, imports, exports, consts);
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

    RecordDecl? ParseRecordDecl()
    {
        if (Eat(TokenKind.Record) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.LBrace) is null) return null;
        var fields = new List<RecordField>();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var fname = Eat(TokenKind.Ident);
            if (fname is null) return null;
            if (Eat(TokenKind.Colon) is null) return null;
            var ftype = ParseType();
            if (ftype is null) return null;
            if (Eat(TokenKind.Semicolon) is null) return null;
            fields.Add(new RecordField(fname.Text, ftype));
        }
        if (Eat(TokenKind.RBrace) is null) return null;
        return new RecordDecl(name.Text, fields);
    }

    ExternFunDecl? ParseExternFunDecl(List<Attr> attrs)
    {
        if (Eat(TokenKind.Extern) is null) return null;
        if (Eat(TokenKind.Fun) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        var parms = ParseParamList();
        if (parms is null) return null;
        if (Eat(TokenKind.Arrow) is null) return null;
        var ret = ParseReturnType();
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
        var ret = ParseReturnType();
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

    ModuleConstDecl? ParseModuleConstDecl()
    {
        if (Eat(TokenKind.Const) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Colon) is null) return null;
        var type = ParseType();
        if (type is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var init = ParseExpr();
        if (init is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ModuleConstDecl(name.Text, type, init);
    }

    ImportDecl? ParseImportDecl()
    {
        if (Eat(TokenKind.Import) is null) return null;
        var modName = Eat(TokenKind.Ident);
        if (modName is null) return null;
        string alias = modName.Text;
        // "as Alias" — 'as' is not a keyword, lexed as Ident
        if (Check(TokenKind.Ident) && Current.Text == "as")
        {
            Advance();
            var aliasTok = Eat(TokenKind.Ident);
            if (aliasTok is null) return null;
            alias = aliasTok.Text;
        }
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ImportDecl(modName.Text, alias);
    }

    string? ParseExportDecl()
    {
        if (Eat(TokenKind.Export) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return name.Text;
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

        if (Check(TokenKind.Hat))
        {
            Advance();
            var inner = ParseType();
            if (inner is null) return null;
            return $"^{inner}";
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

    ReturnStmt? ParseReturnStmt()
    {
        if (Eat(TokenKind.Return) is null) return null;
        if (TryEat(TokenKind.Semicolon)) return new ReturnStmt([]);
        var values = new List<Expr>();
        var first = ParseExpr();
        if (first is null) return null;
        values.Add(first);
        while (TryEat(TokenKind.Comma))
        {
            var next = ParseExpr();
            if (next is null) return null;
            values.Add(next);
        }
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ReturnStmt(values);
    }

    // Parses a function return type: "()" | "(" T1 "," T2 ... ")" | Type
    string? ParseReturnType()
    {
        if (!Check(TokenKind.LParen)) return ParseType();
        Advance(); // consume '('
        if (TryEat(TokenKind.RParen)) return "()";

        // Multi-return: (T1, T2, ...) — optional "name:" prefix on each item (named returns)
        var types = new List<string>();
        do
        {
            // Skip optional "name:" — only when an identifier is immediately followed by ':'
            if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Colon)
            {
                Advance(); // name
                Advance(); // ':'
            }
            var t = ParseType();
            if (t is null) return null;
            types.Add(t);
        } while (TryEat(TokenKind.Comma));

        if (Eat(TokenKind.RParen) is null) return null;
        return "(" + string.Join(",", types) + ")";
    }

    CallExpr? ParseCallArgs(string? qualifier, string name)
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var args     = new List<Expr>();
        var argNames = new List<string?>();
        bool hasNamed = false;
        while (!Check(TokenKind.RParen) && !Check(TokenKind.Eof))
        {
            string? argName = null;
            if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Colon)
            {
                argName  = Current.Text;
                Advance(); // name
                Advance(); // ':'
                hasNamed = true;
            }
            var arg = ParseExpr();
            if (arg is null) return null;
            args.Add(arg);
            argNames.Add(argName);
            if (!TryEat(TokenKind.Comma)) break;
        }
        if (Eat(TokenKind.RParen) is null) return null;
        return new CallExpr(qualifier, name, args, hasNamed ? argNames : null);
    }

    Stmt? ParseStmt()
    {
        if (Check(TokenKind.Return))
            return ParseReturnStmt();
        if (Check(TokenKind.Var))
        {
            if (Peek().Kind == TokenKind.LParen)
                return ParseMultiVarDecl();
            return ParseLocalVarDecl();
        }
        if (Check(TokenKind.LParen))
            return ParseMultiAssignStmt();
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
        if (Check(TokenKind.Const))
            return ParseLocalConstDecl();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Dot)
            return ParseFieldAssignStmt();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Eq)
            return ParseAssignStmt();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.LBracket)
            return ParseIndexAssignStmt();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Hat)
            return ParseDerefAssignStmt();
        if (Check(TokenKind.Ident) && Peek().Kind == TokenKind.Arrow)
            return ParseArrowAssignStmt();
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

    FieldAssignStmt? ParseFieldAssignStmt()
    {
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        var path = new List<string>();
        while (TryEat(TokenKind.Dot))
        {
            var field = Eat(TokenKind.Ident);
            if (field is null) return null;
            path.Add(field.Text);
        }
        if (Eat(TokenKind.Eq) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new FieldAssignStmt(name.Text, path, value);
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

    DerefAssignStmt? ParseDerefAssignStmt()
    {
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Hat) is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new DerefAssignStmt(new VarRefExpr(name.Text), value);
    }

    ArrowAssignStmt? ParseArrowAssignStmt()
    {
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Arrow) is null) return null;
        var field = Eat(TokenKind.Ident);
        if (field is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new ArrowAssignStmt(new VarRefExpr(name.Text), field.Text, value);
    }

    LocalConstDecl? ParseLocalConstDecl()
    {
        if (Eat(TokenKind.Const) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.Colon) is null) return null;
        var type = ParseType();
        if (type is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var init = ParseExpr();
        if (init is null) return null;
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new LocalConstDecl(name.Text, type, init);
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

    // var (a: T, _) = call();
    Stmt? ParseMultiVarDecl()
    {
        if (Eat(TokenKind.Var) is null) return null;
        if (Eat(TokenKind.LParen) is null) return null;
        var items = new List<(string? Name, string? TypeName)>();
        do
        {
            if (Check(TokenKind.Ident) && Current.Text == "_")
            {
                Advance();
                items.Add((null, null));
            }
            else
            {
                var iname = Eat(TokenKind.Ident);
                if (iname is null) return null;
                if (Eat(TokenKind.Colon) is null) return null;
                var itype = ParseType();
                if (itype is null) return null;
                items.Add((iname.Text, itype));
            }
        } while (TryEat(TokenKind.Comma));
        if (Eat(TokenKind.RParen) is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var rhs = ParseExpr();
        if (rhs is null) return null;
        if (rhs is not (CallExpr or DivmodExpr or MulWideExpr))
        {
            Error("expected a function call on the right-hand side of multi-var declaration");
            return null;
        }
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new MultiVarDecl(items, rhs);
    }

    // (a, b) = call();
    Stmt? ParseMultiAssignStmt()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var names = new List<string?>();
        do
        {
            if (Check(TokenKind.Ident) && Current.Text == "_")
            {
                Advance();
                names.Add(null);
            }
            else
            {
                var n = Eat(TokenKind.Ident);
                if (n is null) return null;
                names.Add(n.Text);
            }
        } while (TryEat(TokenKind.Comma));
        if (Eat(TokenKind.RParen) is null) return null;
        if (Eat(TokenKind.Eq) is null) return null;
        var rhs = ParseExpr();
        if (rhs is null) return null;
        if (rhs is not (CallExpr or DivmodExpr or MulWideExpr))
        {
            Error("expected a function call on the right-hand side of multi-assign");
            return null;
        }
        if (Eat(TokenKind.Semicolon) is null) return null;
        return new MultiAssignStmt(names, rhs);
    }

    // ── Builtin expressions ────────────────────────────────────────────────────

    // cast(expr, T)
    Expr? ParseCastExpr()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.Comma) is null) return null;
        var targetType = ParseType();
        if (targetType is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        return new CastExpr(value, targetType);
    }

    // sizeof(T)
    Expr? ParseSizeofExpr()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var typeName = ParseType();
        if (typeName is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        return new SizeofExpr(typeName);
    }

    // length(arrayName)
    Expr? ParseLengthExpr()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var name = Eat(TokenKind.Ident);
        if (name is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        return new LengthExpr(name.Text);
    }

    // to_u8(expr), to_u16(expr), to_u32(expr), to_u64(expr),
    // to_i8(expr), to_i16(expr), to_i32(expr), to_i64(expr)
    Expr? ParseToTypeExpr(string targetType)
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var value = ParseExpr();
        if (value is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        return new ToTypeExpr(value, targetType);
    }

    // divmod(a, b) — tuple-returning: (quotient, remainder)
    Expr? ParseDivmodExpr()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var a = ParseExpr();
        if (a is null) return null;
        if (Eat(TokenKind.Comma) is null) return null;
        var b = ParseExpr();
        if (b is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        return new DivmodExpr(a, b);
    }

    // mul(a, b) — tuple-returning wide multiply: (lo, hi)
    Expr? ParseMulWideExpr()
    {
        if (Eat(TokenKind.LParen) is null) return null;
        var a = ParseExpr();
        if (a is null) return null;
        if (Eat(TokenKind.Comma) is null) return null;
        var b = ParseExpr();
        if (b is null) return null;
        if (Eat(TokenKind.RParen) is null) return null;
        return new MulWideExpr(a, b);
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
        if (Check(TokenKind.At))
        {
            Advance();
            var operand = ParseUnaryExpr();
            return operand is null ? null : new AddressOfExpr(operand);
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

        if (Check(TokenKind.StringLit))
        {
            var tok = Advance();
            return new StringLiteralExpr(DecodeStringLit(tok.Text));
        }

        if (Check(TokenKind.CharLit))
        {
            var tok = Advance();
            return new CharLiteralExpr(DecodeCharLit(tok.Text));
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
            // Qualified call expression: ident.ident(...)
            if (Check(TokenKind.Dot)
                && PeekAt(1).Kind == TokenKind.Ident
                && PeekAt(2).Kind == TokenKind.LParen)
            {
                Advance(); // consume '.'
                var funcTok = Eat(TokenKind.Ident);
                if (funcTok is null) return null;
                return ParseCallArgs(identName, funcTok.Text);
            }
            // Unqualified call expression or builtin: ident(...)
            if (Check(TokenKind.LParen))
                return identName switch
                {
                    "cast"   => ParseCastExpr(),
                    "sizeof" => ParseSizeofExpr(),
                    "length" => ParseLengthExpr(),
                    "divmod" => ParseDivmodExpr(),
                    "mul"    => ParseMulWideExpr(),
                    "to_u8" or "to_u16" or "to_u32" or "to_u64"
                    or "to_i8" or "to_i16" or "to_i32" or "to_i64"
                             => ParseToTypeExpr(identName),
                    _        => ParseCallArgs(null, identName),
                };
            if (Check(TokenKind.Dot))
            {
                var path = new List<string>();
                while (TryEat(TokenKind.Dot))
                {
                    var field = Eat(TokenKind.Ident);
                    if (field is null) return null;
                    path.Add(field.Text);
                }
                return new FieldAccessExpr(identName, path);
            }
            Expr identExpr = new VarRefExpr(identName);
            while (Check(TokenKind.Hat) || Check(TokenKind.Arrow))
            {
                if (TryEat(TokenKind.Hat))
                {
                    identExpr = new DerefExpr(identExpr);
                }
                else
                {
                    Advance(); // consume Arrow
                    var arrowField = Eat(TokenKind.Ident);
                    if (arrowField is null) return null;
                    identExpr = new ArrowExpr(identExpr, arrowField.Text);
                }
            }
            return identExpr;
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

    static byte[] DecodeStringLit(string raw)
    {
        // raw includes surrounding double quotes: "..."
        var bytes = new List<byte>();
        int i   = 1;
        int end = raw.Length - 1;
        while (i < end)
        {
            char c = raw[i];
            if (c == '\\')
            {
                i++;
                if (i >= end) break;
                char esc = raw[i];
                if (esc == 'u' && i + 1 < end && raw[i + 1] == '{')
                {
                    i += 2;
                    int hexStart = i;
                    while (i < end && raw[i] != '}') i++;
                    int codepoint = Convert.ToInt32(raw[hexStart..i], 16);
                    bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(char.ConvertFromUtf32(codepoint)));
                    if (i < end) i++;
                }
                else
                {
                    bytes.Add(DecodeEscapeChar(esc));
                    i++;
                }
            }
            else if (char.IsHighSurrogate(c) && i + 1 < end && char.IsLowSurrogate(raw[i + 1]))
            {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(new string([c, raw[i + 1]])));
                i += 2;
            }
            else
            {
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(c.ToString()));
                i++;
            }
        }
        return [.. bytes];
    }

    static byte DecodeCharLit(string raw)
    {
        // raw includes surrounding single quotes: '.'
        int i = 1;
        if (raw[i] == '\\')
            return DecodeEscapeChar(raw[i + 1]);
        return (byte)raw[i];
    }

    static byte DecodeEscapeChar(char c) => c switch
    {
        'n'  => 10,
        'r'  => 13,
        't'  => 9,
        '\\' => 92,
        '"'  => 34,
        '\'' => 39,
        '0'  => 0,
        _    => (byte)c,
    };

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
