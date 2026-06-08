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

    // Calls exit_process with 2 arguments instead of 1 — type checker must reject this.
    public const string WrongArityCallSource = """
        module main {
            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                exit_process(0, 1);
            }
        }
        """;

    public const string Slice2Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            var exit_code: u32 = 0;

            [win32_entry]
            [noreturn]
            fun main() -> () {
                exit_process(exit_code);
            }
        }
        """;

    public const string Slice3Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var exit_code: u32 = 0;
                exit_process(exit_code);
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

    public const string Slice4Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var a: u32 = 4 + 6;
                var b: u32 = a * 3;
                var c: u32 = b - 10;
                var d: u32 = c / 4;
                var e: u32 = d % 5;
                var f: u32 = 255 & 15;
                var g: u32 = 0 | 0;
                var h: u32 = 15 xor 15;
                var i: u32 = ~(~g);
                var j: u32 = 1 << 4;
                var k: u32 = j >> 4;
                var l: u32 = k - 1;
                var m: u32 = -l;
                var eq:   bool = 5 == 5;
                var ne:   bool = 5 != 4;
                var lt:   bool = 3 < 4;
                var le:   bool = 3 <= 3;
                var gt:   bool = 4 > 3;
                var ge:   bool = 4 >= 4;
                var band: bool = true and true;
                var bor:  bool = false or true;
                var bnot: bool = not false;
                exit_process(m);
            }
        }
        """;

    public const string Slice4PrecedenceSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // mul > add: 2 + 3*4 = 14, not (2+3)*4 = 20
                var t1: u32 = 2 + 3 * 4;
                var c1: u32 = t1 - 14;

                // add > shift: (1+3)<<2 = 16, not 1+(3<<2) = 13
                var t2: u32 = 1 + 3 << 2;
                var c2: u32 = t2 - 16;

                // shift > &: 12 & (2<<1) = 4, not (12&2)<<1 = 0
                var t3: u32 = 12 & 2 << 1;
                var c3: u32 = t3 - 4;

                // & > xor: 4 xor (6&3) = 6, not (4 xor 6)&3 = 2
                var t4: u32 = 4 xor 6 & 3;
                var c4: u32 = t4 - 6;

                // xor > |: 6 | (3 xor 7) = 6, not (6|3) xor 7 = 0
                var t5: u32 = 6 | 3 xor 7;
                var c5: u32 = t5 - 6;

                // parentheses override precedence: (2+3)*4 = 20
                var t6: u32 = (2 + 3) * 4;
                var c6: u32 = t6 - 20;

                // and > or: true or (false and false) = true
                var p1: bool = true or false and false;
                // not > and: (not false) and true = true
                var p2: bool = not false and true;

                // OR all checks: 0 only if every precedence is correct
                var result: u32 = c1 | c2 | c3 | c4 | c5 | c6;
                exit_process(result);
            }
        }
        """;
}
