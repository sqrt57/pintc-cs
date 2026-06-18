using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice22Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice22Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Named_break_exits_outer_while()
    {
        var sourcePath = Path.Combine(_tempDir, "slice22_break_while.pnt");
        var exePath    = Path.Combine(_tempDir, "slice22_break_while.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice22BreakOuterWhileSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Named_continue_skips_to_outer_while()
    {
        var sourcePath = Path.Combine(_tempDir, "slice22_continue.pnt");
        var exePath    = Path.Combine(_tempDir, "slice22_continue.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice22ContinueOuterSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Named_break_exits_labeled_for()
    {
        var sourcePath = Path.Combine(_tempDir, "slice22_labeled_for.pnt");
        var exePath    = Path.Combine(_tempDir, "slice22_labeled_for.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice22LabeledForSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Named_break_exits_labeled_loop()
    {
        var sourcePath = Path.Combine(_tempDir, "slice22_labeled_loop.pnt");
        var exePath    = Path.Combine(_tempDir, "slice22_labeled_loop.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice22LabeledLoopSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
