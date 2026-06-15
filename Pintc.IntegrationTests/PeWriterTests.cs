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
        Code         = [0x6A, 0x00, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00],
        IatRefs      = [new IatRef(CodeOffset: 4, ExitProcess)],
        Imports      = [ExitProcess],
        Data         = [],
        ReadOnly     = [],
        ExportedFuns = [],
    };

    // .data at 0x00402000 holds u32 = 5; code reads it and passes to ExitProcess.
    static readonly CodeUnit Slice2Unit = new()
    {
        Code         = [0xFF, 0x35, 0x00, 0x20, 0x40, 0x00,  // push dword ptr [0x00402000]
                        0xFF, 0x15, 0x00, 0x00, 0x00, 0x00], // call [IAT_ExitProcess]
        IatRefs      = [new IatRef(CodeOffset: 8, ExitProcess)],
        Imports      = [ExitProcess],
        Data         = [5, 0, 0, 0],
        ReadOnly     = [],
        ExportedFuns = [],
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

    [Fact]
    public void HardcodedSlice2_ReadsDataSectionAndExitsWith5()
    {
        var exePath = Path.Combine(_tempDir, "slice2_pe.exe");

        using (var fs = File.Create(exePath))
            PeWriter.Write(Slice2Unit, fs);

        using var proc = Process.Start(new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        })!;
        proc.WaitForExit();

        proc.ExitCode.ShouldBe(5);
    }
}
