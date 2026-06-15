using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice14Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice14Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Char_literals()
    {
        var sourcePath = Path.Combine(_tempDir, "slice14_chars.pnt");
        var exePath    = Path.Combine(_tempDir, "slice14_chars.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice14CharLiteralsSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void String_len()
    {
        var sourcePath = Path.Combine(_tempDir, "slice14_len.pnt");
        var exePath    = Path.Combine(_tempDir, "slice14_len.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice14StringLenSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void String_ptr_bytes()
    {
        var sourcePath = Path.Combine(_tempDir, "slice14_ptr.pnt");
        var exePath    = Path.Combine(_tempDir, "slice14_ptr.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice14StringPtrSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void String_write_to_stdout()
    {
        var sourcePath = Path.Combine(_tempDir, "slice14_write.pnt");
        var exePath    = Path.Combine(_tempDir, "slice14_write.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice14StringWriteSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
        run.Stdout.ShouldBe("Hello, Pint!");
    }
}
