using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class ResolverErrorTests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ResolverErrorTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Unknown_callee_exits_nonzero_with_error_message()
    {
        var sourcePath = Path.Combine(_tempDir, "unknown_callee.pnt");
        var exePath    = Path.Combine(_tempDir, "unknown_callee.exe");
        File.WriteAllText(sourcePath, SliceFixtures.UnknownCalleeSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);

        compile.ExitCode.ShouldNotBe(0);
        compile.Stderr.ShouldContain("unknown_func");
    }
}
