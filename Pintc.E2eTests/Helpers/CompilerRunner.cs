using System.Diagnostics;

namespace Pintc.E2eTests.Helpers;

static class CompilerRunner
{
    // pintc-cs/Pintc/Pintc.csproj, resolved from the test output directory
    static readonly string PintcProject = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../Pintc/Pintc.csproj"));

    public static ProcessResult Compile(string sourceFile, string outputFile)
    {
        var pintcExe = Environment.GetEnvironmentVariable("PINTC_EXE");

        ProcessStartInfo psi;
        if (pintcExe is not null)
        {
            psi = new ProcessStartInfo(pintcExe, $"\"{sourceFile}\" -o \"{outputFile}\"");
        }
        else
        {
            psi = new ProcessStartInfo(
                "dotnet",
                $"run --project \"{PintcProject}\" -- \"{sourceFile}\" -o \"{outputFile}\"");
        }

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
