namespace Pintc.TestFixtures;

public static class SliceFixtures
{
    // Calls an undeclared function — resolver must reject this.
    public const string UnknownCalleeSource = """
        module main {
            [win32_entry]
            [noreturn]
            fun main() -> () {
                unknown_func(0);
            }
        }
        """;

    public const string Slice1Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                exit_process(0);
            }
        }
        """;
}
