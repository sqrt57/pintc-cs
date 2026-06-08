using System.Diagnostics;

namespace Pintc.IntegrationTests;

// Verifies the PE32 writer by feeding it a hardcoded CodeUnit representing slice 1:
//   push 0  /  call [ExitProcess]
// The resulting EXE must run on Windows and exit with code 0.
public class PeWriterTests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public PeWriterTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    static readonly ImportSpec ExitProcess = new("kernel32.dll", "ExitProcess");

    // code bytes for: push 0 / call [IAT_ExitProcess]
    // bytes 4-7 are the IAT address placeholder, patched by PeWriter
    static readonly CodeUnit Slice1Unit = new()
    {
        Code    = [0x6A, 0x00, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00],
        IatRefs = [new IatRef(CodeOffset: 4, ExitProcess)],
        Imports = [ExitProcess],
    };

    [Fact]
    public void HardcodedSlice1_RunsAndExitsWith0()
    {
        var exePath = Path.Combine(_tempDir, "slice1.exe");

        using (var fs = File.Create(exePath))
            PeWriter.Write(Slice1Unit, fs);

        using var proc = Process.Start(new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        })!;
        proc.WaitForExit();

        proc.ExitCode.ShouldBe(0);
    }
}
