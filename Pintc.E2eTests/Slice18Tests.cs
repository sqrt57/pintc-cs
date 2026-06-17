using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice18Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice18Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Named_return_statement()
    {
        var sourcePath = Path.Combine(_tempDir, "slice18_named_return.pnt");
        var exePath    = Path.Combine(_tempDir, "slice18_named_return.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice18NamedReturnSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Named_unpack()
    {
        var sourcePath = Path.Combine(_tempDir, "slice18_named_unpack.pnt");
        var exePath    = Path.Combine(_tempDir, "slice18_named_unpack.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice18NamedUnpackSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

}
