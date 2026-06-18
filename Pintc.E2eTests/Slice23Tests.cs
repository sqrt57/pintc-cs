using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice23Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice23Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void F64_arithmetic_add_sub_mul_div()
    {
        var sourcePath = Path.Combine(_tempDir, "slice23_f64_arith.pnt");
        var exePath    = Path.Combine(_tempDir, "slice23_f64_arith.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice23F64ArithmeticSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void F32_arithmetic()
    {
        var sourcePath = Path.Combine(_tempDir, "slice23_f32_arith.pnt");
        var exePath    = Path.Combine(_tempDir, "slice23_f32_arith.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice23F32ArithmeticSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void F64_comparison_operators()
    {
        var sourcePath = Path.Combine(_tempDir, "slice23_f64_cmp.pnt");
        var exePath    = Path.Combine(_tempDir, "slice23_f64_cmp.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice23F64ComparisonSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void F64_unary_negate()
    {
        var sourcePath = Path.Combine(_tempDir, "slice23_f64_negate.pnt");
        var exePath    = Path.Combine(_tempDir, "slice23_f64_negate.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice23F64NegateSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void F64_exponent_literals()
    {
        var sourcePath = Path.Combine(_tempDir, "slice23_f64_exp.pnt");
        var exePath    = Path.Combine(_tempDir, "slice23_f64_exp.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice23F64ExponentSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void F64_as_function_parameter_and_return()
    {
        var sourcePath = Path.Combine(_tempDir, "slice23_f64_fun.pnt");
        var exePath    = Path.Combine(_tempDir, "slice23_f64_fun.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice23F64FunctionSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
