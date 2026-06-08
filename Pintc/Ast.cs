namespace Pintc;

// Pint source attribute, e.g. [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
record Attr(string Name, Dictionary<string, string> Args)
{
    public Attr(string name) : this(name, []) { }
    public string? Get(string key) => Args.TryGetValue(key, out var v) ? v : null;
}

record Param(string Name, string TypeName);

abstract record Expr;
record IntLiteralExpr(long Value) : Expr;

// A call statement: callee(arg0, arg1, ...)
record CallStmt(string Callee, List<Expr> Args);

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
    List<CallStmt> Body);

record ModuleDecl(
    string Name,
    List<ExternFunDecl> Externs,
    List<FunDecl> Funs);
