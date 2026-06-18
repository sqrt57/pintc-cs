using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice20Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice20Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Record_literal_fields_in_order()
    {
        var sourcePath = Path.Combine(_tempDir, "slice20.pnt");
        var exePath    = Path.Combine(_tempDir, "slice20.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice20Source);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Record_literal_fields_out_of_order()
    {
        var sourcePath = Path.Combine(_tempDir, "slice20_outoforder.pnt");
        var exePath    = Path.Combine(_tempDir, "slice20_outoforder.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice20OutOfOrderSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Record_literal_nested()
    {
        var sourcePath = Path.Combine(_tempDir, "slice20_nested.pnt");
        var exePath    = Path.Combine(_tempDir, "slice20_nested.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice20NestedSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
