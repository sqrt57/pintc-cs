using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice12Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice12Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Const_initializer_evaluated_once()
    {
        var sourcePath = Path.Combine(_tempDir, "slice12_reeval.pnt");
        var exePath    = Path.Combine(_tempDir, "slice12_reeval.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice12ConstReevalSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Local_constants()
    {
        var sourcePath = Path.Combine(_tempDir, "slice12.pnt");
        var exePath    = Path.Combine(_tempDir, "slice12.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice12Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
