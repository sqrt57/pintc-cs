using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice3Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice3Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Compiles_and_exits_with_0()
    {
        var sourcePath = Path.Combine(_tempDir, "slice3.pnt");
        var exePath    = Path.Combine(_tempDir, "slice3.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice3Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
