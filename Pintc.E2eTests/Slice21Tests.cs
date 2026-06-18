using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice21Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice21Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Enum_basic_variant_and_equality()
    {
        var sourcePath = Path.Combine(_tempDir, "slice21.pnt");
        var exePath    = Path.Combine(_tempDir, "slice21.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice21BasicSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Enum_explicit_values_and_cast_to_int()
    {
        var sourcePath = Path.Combine(_tempDir, "slice21_explicit.pnt");
        var exePath    = Path.Combine(_tempDir, "slice21_explicit.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice21ExplicitValuesSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Enum_cast_from_int()
    {
        var sourcePath = Path.Combine(_tempDir, "slice21_castfrom.pnt");
        var exePath    = Path.Combine(_tempDir, "slice21_castfrom.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice21CastFromIntSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Enum_explicit_underlying_type_u8()
    {
        var sourcePath = Path.Combine(_tempDir, "slice21_u8.pnt");
        var exePath    = Path.Combine(_tempDir, "slice21_u8.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice21UnderlyingTypeSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
