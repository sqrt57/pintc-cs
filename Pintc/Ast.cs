namespace Pintc;

// Pint source attribute, e.g. [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
record Attr(string Name, Dictionary<string, string> Args)
{
    public Attr(string name) : this(name, []) { }
    public string? Get(string key) => Args.TryGetValue(key, out var v) ? v : null;
}

record Param(string Name, string TypeName);

enum BinaryOp
{
    Add, Sub, Mul, Div, Mod,
    BitAnd, BitOr, BitXor,
    And, Or,
    Shl, Shr,
    Eq, Ne, Lt, Le, Gt, Ge,
}

enum UnaryOp { Neg, BitNot, Not }

abstract record Expr;
record IntLiteralExpr(long Value) : Expr;
record BoolLiteralExpr(bool Value) : Expr;
record VarRefExpr(string Name) : Expr;
record BinaryExpr(BinaryOp Op, Expr Left, Expr Right) : Expr;
record UnaryExpr(UnaryOp Op, Expr Operand) : Expr;

abstract record Stmt;
record CallStmt(string Callee, List<Expr> Args) : Stmt;
record LocalVarDecl(string Name, string TypeName, Expr? Init) : Stmt;

// An extern function imported from a DLL.
record ExternFunDecl(
    List<Attr> Attributes,
    string Name,
    List<Param> Params,
    string ReturnType);

// A user-defined function.
record FunDecl(
    List<Attr> Attributes,
    string Name,
    List<Param> Params,
    string ReturnType,
    List<Stmt> Body);

// A module-scope variable declaration.
record ModuleVarDecl(string Name, string TypeName, Expr? Init);

record ModuleDecl(
    string Name,
    List<ExternFunDecl> Externs,
    List<FunDecl> Funs,
    List<ModuleVarDecl> Vars);
