using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice15Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice15Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Two_values_returned()
    {
        var sourcePath = Path.Combine(_tempDir, "slice15.pnt");
        var exePath    = Path.Combine(_tempDir, "slice15.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice15Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Discard_one_return_value()
    {
        var sourcePath = Path.Combine(_tempDir, "slice15_discard.pnt");
        var exePath    = Path.Combine(_tempDir, "slice15_discard.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice15DiscardSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Multi_assign_to_existing_vars()
    {
        var sourcePath = Path.Combine(_tempDir, "slice15_multiassign.pnt");
        var exePath    = Path.Combine(_tempDir, "slice15_multiassign.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice15MultiAssignSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
