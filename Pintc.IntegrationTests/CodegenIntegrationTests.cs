using System.Diagnostics;

namespace Pintc.IntegrationTests;

// Verifies that Codegen.Emit produces a CodeUnit that, when passed through PeWriter,
// yields a working EXE — end-to-end for codegen without a real parser.
public class CodegenIntegrationTests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public CodegenIntegrationTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    static readonly ModuleDecl Slice1Module = new(
        "main",
        Externs:
        [
            new ExternFunDecl(
                [new Attr("dll_import", new() { ["dll"] = "kernel32.dll", ["entry_point"] = "ExitProcess" }),
                 new Attr("noreturn")],
                "exit_process",
                [new Param("code", "u32")],
                "()")
        ],
        Funs:
        [
            new FunDecl(
                [new Attr("win32_entry"), new Attr("noreturn")],
                "main",
                [],
                "()",
                [new CallStmt("exit_process", [new IntLiteralExpr(0)])])
        ],
        Vars: []);

    static readonly ModuleDecl Slice2Module = new(
        "main",
        Externs:
        [
            new ExternFunDecl(
                [new Attr("dll_import", new() { ["dll"] = "kernel32.dll", ["entry_point"] = "ExitProcess" }),
                 new Attr("noreturn")],
                "exit_process",
                [new Param("code", "u32")],
                "()")
        ],
        Funs:
        [
            new FunDecl(
                [new Attr("win32_entry"), new Attr("noreturn")],
                "main",
                [],
                "()",
                [new CallStmt("exit_process", [new VarRefExpr("exit_code")])])
        ],
        Vars: [new ModuleVarDecl("exit_code", "u32", new IntLiteralExpr(3))]);

    [Fact]
    public void Slice1_codegenAndPeWriter_produceRunningExe()
    {
        var exePath = Path.Combine(_tempDir, "slice1_codegen.exe");

        var unit = Codegen.Emit(Slice1Module);

        using (var fs = File.Create(exePath))
            PeWriter.Write(unit, fs);

        using var proc = Process.Start(new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        })!;
        proc.WaitForExit();

        proc.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Slice2_moduleVar_valueReadAtRuntime()
    {
        var exePath = Path.Combine(_tempDir, "slice2_codegen.exe");

        var unit = Codegen.Emit(Slice2Module);

        using (var fs = File.Create(exePath))
            PeWriter.Write(unit, fs);

        using var proc = Process.Start(new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        })!;
        proc.WaitForExit();

        proc.ExitCode.ShouldBe(3);
    }
}
