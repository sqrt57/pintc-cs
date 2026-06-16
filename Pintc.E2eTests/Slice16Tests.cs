using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice16Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice16Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Cast_and_to_type()
    {
        var sourcePath = Path.Combine(_tempDir, "slice16_cast.pnt");
        var exePath    = Path.Combine(_tempDir, "slice16_cast.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice16CastSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Sizeof_and_length()
    {
        var sourcePath = Path.Combine(_tempDir, "slice16_sizeof.pnt");
        var exePath    = Path.Combine(_tempDir, "slice16_sizeof.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice16SizeofLengthSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Divmod_native()
    {
        var sourcePath = Path.Combine(_tempDir, "slice16_divmod.pnt");
        var exePath    = Path.Combine(_tempDir, "slice16_divmod.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice16DivmodSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Mul_wide()
    {
        var sourcePath = Path.Combine(_tempDir, "slice16_mul.pnt");
        var exePath    = Path.Combine(_tempDir, "slice16_mul.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice16MulSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
