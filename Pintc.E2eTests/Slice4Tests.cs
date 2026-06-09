using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice4Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice4Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Compiles_and_exits_with_0()
    {
        var sourcePath = Path.Combine(_tempDir, "slice4.pnt");
        var exePath    = Path.Combine(_tempDir, "slice4.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice4Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Precedence_compiles_and_exits_with_0()
    {
        var sourcePath = Path.Combine(_tempDir, "slice4-precedence.pnt");
        var exePath    = Path.Combine(_tempDir, "slice4-precedence.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice4PrecedenceSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
