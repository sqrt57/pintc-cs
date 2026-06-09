using System.Diagnostics;

namespace Pintc.E2eTests.Helpers;

static class CompilerRunner
{
    // pintc-cs/Pintc/Pintc.csproj, resolved from the test output directory
    static readonly string PintcProject = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../Pintc/Pintc.csproj"));

    public static ProcessResult Compile(string sourceFile, string outputFile)
        => RunCompiler(BuildArgs([sourceFile], outputFile));

    public static ProcessResult Compile(IReadOnlyList<string> sourceFiles, string outputFile)
        => RunCompiler(BuildArgs(sourceFiles, outputFile));

    public static ProcessResult CompileDll(string sourceFile, string outputFile)
        => RunCompiler(BuildArgs([sourceFile], outputFile, "--dll"));

    static string BuildArgs(IReadOnlyList<string> sourceFiles, string outputFile, string flags = "")
    {
        var sources = string.Join(" ", sourceFiles.Select(f => $"\"{f}\""));
        return string.IsNullOrEmpty(flags)
            ? $"{sources} -o \"{outputFile}\""
            : $"{flags} {sources} -o \"{outputFile}\"";
    }

    static ProcessResult RunCompiler(string args)
    {
        var pintcExe = Environment.GetEnvironmentVariable("PINTC_EXE");
        ProcessStartInfo psi = pintcExe is not null
            ? new ProcessStartInfo(pintcExe, args)
            : new ProcessStartInfo("dotnet", $"run --project \"{PintcProject}\" -- {args}");
        return Run(psi);
    }

    public static ProcessResult Execute(string exePath) =>
        Run(new ProcessStartInfo(exePath));

    static ProcessResult Run(ProcessStartInfo psi)
    {
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;

        using var process = Process.Start(psi)!;
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, stdout, stderr);
    }
}

record ProcessResult(int ExitCode, string Stdout, string Stderr);
