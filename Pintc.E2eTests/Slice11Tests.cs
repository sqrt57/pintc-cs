using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class Slice11Tests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public Slice11Tests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Import_export_and_cross_module_resolution()
    {
        var sourcePath = Path.Combine(_tempDir, "slice11.pnt");
        var exePath    = Path.Combine(_tempDir, "slice11.exe");
        File.WriteAllText(sourcePath, SliceFixtures.Slice11TwoModulesSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Multi_file_compilation()
    {
        var calcPath = Path.Combine(_tempDir, "calc.pnt");
        var mainPath = Path.Combine(_tempDir, "main.pnt");
        var exePath  = Path.Combine(_tempDir, "slice11.exe");
        File.WriteAllText(calcPath, SliceFixtures.Slice11CalcSource);
        File.WriteAllText(mainPath, SliceFixtures.Slice11MainSource);

        var compile = CompilerRunner.Compile([calcPath, mainPath], exePath);
        compile.ExitCode.ShouldBe(0, compile.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }

    [Fact]
    public void Dll_output_and_import()
    {
        var libPath  = Path.Combine(_tempDir, "mathlib.pnt");
        var mainPath = Path.Combine(_tempDir, "main.pnt");
        var dllPath  = Path.Combine(_tempDir, "mathlib.dll");
        var exePath  = Path.Combine(_tempDir, "main.exe");
        File.WriteAllText(libPath,  SliceFixtures.Slice11DllLibSource);
        File.WriteAllText(mainPath, SliceFixtures.Slice11DllMainSource);

        var compileDll = CompilerRunner.CompileDll(libPath, dllPath);
        compileDll.ExitCode.ShouldBe(0, compileDll.Stderr);

        var compileExe = CompilerRunner.Compile(mainPath, exePath);
        compileExe.ExitCode.ShouldBe(0, compileExe.Stderr);

        var run = CompilerRunner.Execute(exePath);
        run.ExitCode.ShouldBe(0);
    }
}
