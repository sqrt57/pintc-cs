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
record CallExpr(string? Qualifier, string Name, List<Expr> Args, List<string?>? ArgNames) : Expr
{
    public CallExpr(string? qualifier, string name, List<Expr> args)
        : this(qualifier, name, args, null) { }
}
record IntLiteralExpr(long Value) : Expr;
record BoolLiteralExpr(bool Value) : Expr;
record VarRefExpr(string Name) : Expr;
record BinaryExpr(BinaryOp Op, Expr Left, Expr Right) : Expr;
record UnaryExpr(UnaryOp Op, Expr Operand) : Expr;
record IndexExpr(string ArrayName, Expr Idx) : Expr;
record FieldAccessExpr(string VarName, List<string> Path) : Expr;
record AddressOfExpr(Expr Operand) : Expr;
record DerefExpr(Expr Ptr) : Expr;
record ArrowExpr(Expr Ptr, string Field) : Expr;
record CharLiteralExpr(byte Value) : Expr;
record StringLiteralExpr(byte[] Bytes) : Expr;
record StringConstExpr(uint RdataOffset, int ByteCount) : Expr;
record CastExpr(Expr Value, string TargetType) : Expr;
record SizeofExpr(string TypeName) : Expr;
record LengthExpr(string ArrayName) : Expr;
record ToTypeExpr(Expr Value, string TargetType) : Expr;
record DivmodExpr(Expr A, Expr B) : Expr;
record MulWideExpr(Expr A, Expr B) : Expr;
record ArrayLiteralExpr(List<Expr> Elements) : Expr;
record RecordLiteralExpr(List<(string Field, Expr Value)> Fields) : Expr;
record FloatLiteralExpr(double Value) : Expr;

abstract record Stmt;
record CallStmt(string Callee, List<Expr> Args) : Stmt;
record ReturnStmt(List<Expr> Values, List<string?>? ReturnNames) : Stmt
{
    public ReturnStmt(List<Expr> values) : this(values, null) { }
}
record LocalVarDecl(string Name, string TypeName, Expr? Init) : Stmt;
record AssignStmt(string Name, Expr Value) : Stmt;
record IndexAssignStmt(string ArrayName, Expr Idx, Expr Value) : Stmt;
record FieldAssignStmt(string VarName, List<string> Path, Expr Value) : Stmt;
record DerefAssignStmt(Expr Ptr, Expr Value) : Stmt;
record ArrowAssignStmt(Expr Ptr, string Field, Expr Value) : Stmt;
record IfStmt(Expr Condition, List<Stmt> Then, List<Stmt>? Else) : Stmt;
record WhileStmt(Expr Condition, List<Stmt> Body, string? Label = null) : Stmt;
record LoopStmt(List<Stmt> Body, string? Label = null) : Stmt;
record BreakStmt(string? Label = null) : Stmt;
record ContinueStmt(string? Label = null) : Stmt;
record ForStmt(string VarName, string VarTypeName, Expr VarInit, Expr Condition, string PostName, Expr PostValue, List<Stmt> Body, string? Label = null) : Stmt;
record LocalConstDecl(string Name, string TypeName, Expr Init) : Stmt;
// var (a: T, _) = call(); — introduces new locals from a multi-return call.
// Items: (Name=null, TypeName=null) for discard (_), (Name, TypeName) otherwise.
// RHS is CallExpr for user-defined functions or DivmodExpr/MulWideExpr for builtins.
record MultiVarDecl(List<(string? Name, string? TypeName)> Items, Expr Call) : Stmt;
// (a, b) = call(); — positional assign into existing locals from a multi-return call.
// (ret: a, ret2: b) = call(); — named assign; ReturnNames holds the return-side names.
// Names: null for discard (_), variable name otherwise.
// RHS is CallExpr for user-defined functions or DivmodExpr/MulWideExpr for builtins.
record MultiAssignStmt(List<string?> Names, Expr Call, List<string?>? ReturnNames) : Stmt
{
    public MultiAssignStmt(List<string?> names, Expr call) : this(names, call, null) { }
}

record RecordField(string Name, string TypeName);
record RecordDecl(string Name, List<RecordField> Fields);

record EnumVariant(string Name, Expr? Value);
record EnumDecl(string Name, string? UnderlyingType, List<EnumVariant> Variants);

// An extern function imported from a DLL.
record ExternFunDecl(
    List<Attr> Attributes,
    string Name,
    List<Param> Params,
    string ReturnType);

// A user-defined function.
// ReturnNames: null or empty for positional returns; non-null list of optional names for named returns.
record FunDecl(
    List<Attr> Attributes,
    string Name,
    List<Param> Params,
    string ReturnType,
    List<Stmt> Body,
    List<string?>? ReturnNames)
{
    public FunDecl(List<Attr> attrs, string name, List<Param> parms, string ret, List<Stmt> body)
        : this(attrs, name, parms, ret, body, null) { }
}

// A module-scope variable declaration.
record ModuleVarDecl(string Name, string TypeName, Expr? Init);

// A module-scope constant declaration.
record ModuleConstDecl(string Name, string TypeName, Expr Init);

record ImportDecl(string ModuleName, string Alias);

record ModuleDecl(
    string Name,
    List<ExternFunDecl> Externs,
    List<FunDecl> Funs,
    List<ModuleVarDecl> Vars,
    List<RecordDecl> Records,
    List<ImportDecl> Imports,
    List<string> Exports,
    List<ModuleConstDecl> Consts,
    List<EnumDecl> Enums)
{
    // Backwards-compatible constructor for test fixtures that don't use import/export/consts/enums.
    public ModuleDecl(
        string Name,
        List<ExternFunDecl> Externs,
        List<FunDecl> Funs,
        List<ModuleVarDecl> Vars,
        List<RecordDecl> Records)
        : this(Name, Externs, Funs, Vars, Records, [], [], [], []) { }
}
