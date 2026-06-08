using Pintc.E2eTests.Helpers;
using Pintc.TestFixtures;

namespace Pintc.E2eTests;

public class TypeCheckerErrorTests : IDisposable
{
    readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public TypeCheckerErrorTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Wrong_arity_call_exits_nonzero_with_error_message()
    {
        var sourcePath = Path.Combine(_tempDir, "wrong_arity.pnt");
        var exePath    = Path.Combine(_tempDir, "wrong_arity.exe");
        File.WriteAllText(sourcePath, SliceFixtures.WrongArityCallSource);

        var compile = CompilerRunner.Compile(sourcePath, exePath);

        compile.ExitCode.ShouldNotBe(0);
        compile.Stderr.ShouldContain("exit_process");
        compile.Stderr.ShouldContain("argument");
    }
}
