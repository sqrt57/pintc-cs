using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice19Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice19Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Array_literal_read()
    {
        var sourcePath = Path.Combine(_tempDir, "slice19.pnt");
        var exePath    = Path.Combine(_tempDir, "slice19.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice19Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Array_literal_write_after_init()
    {
        var sourcePath = Path.Combine(_tempDir, "slice19_write.pnt");
        var exePath    = Path.Combine(_tempDir, "slice19_write.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice19WriteSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
