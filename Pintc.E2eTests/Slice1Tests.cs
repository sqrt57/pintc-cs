using Pintc.E2eTests.Helpers;

namespace Pintc.E2eTests;

public class Slice1Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice1Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    const string Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                exit_process(0);
            }
        }
        """;

    [Fact]
    public void Compiles_and_exits_with_0()
    {
        var sourcePath = Path.Combine(_tempDir, "slice1.pnt");
        var exePath    = Path.Combine(_tempDir, "slice1.exe");
        File.WriteAllText(sourcePath, Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
