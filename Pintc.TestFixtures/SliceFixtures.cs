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

    public const string Slice5Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // if not taken: false condition skips body
                if (false) { exit_process(1); }

                // if/else: false condition takes else-branch
                if (false) { exit_process(2); } else { }

                // if/else: true condition takes then-branch
                if (true) { } else { exit_process(3); }

                // condition from expression
                var a: u32 = 4 + 3;
                if (a != 7) { exit_process(4); }

                // else if chain: middle branch matches
                var x: u32 = 2;
                if (x == 1) {
                    exit_process(5);
                } else if (x == 2) {
                } else {
                    exit_process(6);
                }

                // nested if/else
                var p: u32 = 5;
                var q: u32 = 10;
                if (p < q) {
                    if (p != q) {
                    } else {
                        exit_process(7);
                    }
                } else {
                    exit_process(8);
                }

                exit_process(0);
            }
        }
        """;

    public const string Slice6Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // while: count up to 3
                var i: u32 = 0;
                while (i < 3) {
                    i = i + 1;
                }
                if (i != 3) { exit_process(1); }

                // while with false condition: body never executes
                var j: u32 = 0;
                while (false) {
                    j = j + 1;
                }
                if (j != 0) { exit_process(2); }

                // loop + break
                var k: u32 = 0;
                loop {
                    k = k + 1;
                    if (k == 3) { break; }
                }
                if (k != 3) { exit_process(3); }

                // continue: accumulate only odd values (1 and 3) out of 1..4
                var n: u32 = 0;
                var sum: u32 = 0;
                while (n < 4) {
                    n = n + 1;
                    if (n % 2 == 0) { continue; }
                    sum = sum + n;
                }
                if (sum != 4) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice8Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // element assignment and read with constant indices
                var a: [3]u32;
                a[0] = 10;
                a[1] = 20;
                a[2] = 30;
                if (a[0] != 10) { exit_process(1); }
                if (a[1] != 20) { exit_process(2); }
                if (a[2] != 30) { exit_process(3); }

                // sum via for loop (variable index read)
                var sum: u32 = 0;
                for (var i: u32 = 0; i < 3; i = i + 1) {
                    sum = sum + a[i];
                }
                if (sum != 60) { exit_process(4); }

                // fill with squares via for loop (variable index write)
                var b: [4]u32;
                for (var i: u32 = 0; i < 4; i = i + 1) {
                    b[i] = i * i;
                }
                if (b[0] != 0) { exit_process(5); }
                if (b[1] != 1) { exit_process(6); }
                if (b[2] != 4) { exit_process(7); }
                if (b[3] != 9) { exit_process(8); }

                exit_process(0);
            }
        }
        """;

    public const string Slice7Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // for: count up to 3
                var count: u32 = 0;
                for (var i: u32 = 0; i < 3; i = i + 1) {
                    count = count + 1;
                }
                if (count != 3) { exit_process(1); }

                // for with false initial condition: body never executes
                var skipped: u32 = 0;
                for (var i: u32 = 5; i < 3; i = i + 1) {
                    skipped = skipped + 1;
                }
                if (skipped != 0) { exit_process(2); }

                // for with break: body runs until i == 2 (i = 0, 1 counted)
                var broke: u32 = 0;
                for (var i: u32 = 0; i < 5; i = i + 1) {
                    if (i == 2) { break; }
                    broke = broke + 1;
                }
                if (broke != 2) { exit_process(3); }

                // for with continue: post-step runs on continue; accumulate odd i from 0..3 (1+3=4)
                var sum: u32 = 0;
                for (var i: u32 = 0; i < 4; i = i + 1) {
                    if (i % 2 == 0) { continue; }
                    sum = sum + i;
                }
                if (sum != 4) { exit_process(4); }

                // nested for: 3x3 = 9 total iterations
                var nested: u32 = 0;
                for (var i: u32 = 0; i < 3; i = i + 1) {
                    for (var j: u32 = 0; j < 3; j = j + 1) {
                        nested = nested + 1;
                    }
                }
                if (nested != 9) { exit_process(5); }

                exit_process(0);
            }
        }
        """;

    public const string Slice9Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            record Point {
                x: u32;
                y: u32;
            }

            record Rect {
                left:   u32;
                top:    u32;
                right:  u32;
                bottom: u32;
            }

            record Line {
                start: Point;
                end:   Point;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // field assignment and read
                var p: Point;
                p.x = 10;
                p.y = 20;
                if (p.x != 10) { exit_process(1); }
                if (p.y != 20) { exit_process(2); }

                // field in arithmetic expression
                var sum: u32 = p.x + p.y;
                if (sum != 30) { exit_process(3); }

                // two locals of same record type: fields are independent
                var a: Point;
                var b: Point;
                a.x = 1;
                b.x = 2;
                if (a.x != 1) { exit_process(4); }
                if (b.x != 2) { exit_process(5); }

                // four-field record
                var r: Rect;
                r.left   = 0;
                r.top    = 5;
                r.right  = 100;
                r.bottom = 200;
                var width:  u32 = r.right - r.left;
                var height: u32 = r.bottom - r.top;
                if (width  != 100) { exit_process(6); }
                if (height != 195) { exit_process(7); }

                // nested record: Line contains two Points
                var ln: Line;
                ln.start.x = 3;
                ln.start.y = 4;
                ln.end.x   = 9;
                ln.end.y   = 12;
                var dx: u32 = ln.end.x - ln.start.x;
                var dy: u32 = ln.end.y - ln.start.y;
                if (dx != 6) { exit_process(8); }
                if (dy != 8) { exit_process(9); }

                exit_process(0);
            }
        }
        """;

    public const string Slice10Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            record Point {
                x: u32;
                y: u32;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // address-of scalar, dereference read
                var x: u32 = 42;
                var p: ^u32 = @x;
                if (p^ != 42) { exit_process(1); }

                // write through pointer: alias update visible via original variable
                p^ = 99;
                if (x != 99) { exit_process(2); }

                // pointer to record, arrow read
                var pt: Point;
                pt.x = 10;
                pt.y = 20;
                var pp: ^Point = @pt;
                if (pp->x != 10) { exit_process(3); }
                if (pp->y != 20) { exit_process(4); }

                // arrow write: update through pointer visible via original record
                pp->x = 55;
                if (pt.x != 55) { exit_process(5); }

                // pointer arithmetic: read elements via advancing pointer
                var arr: [3]u32;
                arr[0] = 11;
                arr[1] = 22;
                arr[2] = 33;
                var pa: ^u32 = @arr;
                if (pa^ != 11) { exit_process(6); }
                var pb: ^u32 = pa + 1;
                if (pb^ != 22) { exit_process(7); }
                var pc: ^u32 = pa + 2;
                if (pc^ != 33) { exit_process(8); }

                // pointer arithmetic: write through advanced pointer
                var buf: [2]u32;
                var pw: ^u32 = @buf;
                pw^ = 5;
                var qw: ^u32 = pw + 1;
                qw^ = 10;
                if (buf[0] != 5)  { exit_process(9); }
                if (buf[1] != 10) { exit_process(10); }

                exit_process(0);
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

    public const string Slice11TwoModulesSource = """
        module Calc {
            fun add(a: u32, b: u32) -> u32 {
                return a + b;
            }
            export add;
        }

        module main {
            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            import Calc as C;

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var result: u32 = C.add(3, 4);
                if (result != 7) { exit_process(1); }
                exit_process(0);
            }
        }
        """;

    public const string Slice11CalcSource = """
        module Calc {
            fun add(a: u32, b: u32) -> u32 {
                return a + b;
            }
            export add;
        }
        """;

    public const string Slice11MainSource = """
        module main {
            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            import Calc as C;

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var result: u32 = C.add(3, 4);
                if (result != 7) { exit_process(1); }
                exit_process(0);
            }
        }
        """;

    public const string Slice11DllLibSource = """
        module MathLib {
            [dll_export]
            fun add(a: u32, b: u32) -> u32 {
                return a + b;
            }
        }
        """;

    public const string Slice11DllMainSource = """
        module main {
            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [dll_import(dll="mathlib.dll", entry_point="add")]
            extern fun math_add(a: u32, b: u32) -> u32;

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var result: u32 = math_add(3, 4);
                if (result != 7) { exit_process(1); }
                exit_process(0);
            }
        }
        """;

    public const string Slice12Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun get_base() -> u32 {
                return 3;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // const used in an expression
                const LIMIT: u32 = 10;
                var x: u32 = LIMIT + 5;
                if (x != 15) { exit_process(1); }

                // const used in a comparison
                const THRESH: u32 = 7;
                var y: u32 = 8;
                if (y <= THRESH) { exit_process(2); }

                // const bool
                const OK: bool = true;
                if (not OK) { exit_process(3); }

                // two consts combined in arithmetic
                const WIDTH: u32 = 4;
                const HEIGHT: u32 = 5;
                var area: u32 = WIDTH * HEIGHT;
                if (area != 20) { exit_process(4); }

                // const initialized from a function call
                const BASE: u32 = get_base();
                var z: u32 = BASE * 2;
                if (z != 6) { exit_process(5); }

                // const passed directly as a call argument
                const EXIT_OK: u32 = 0;
                exit_process(EXIT_OK);
            }
        }
        """;

    public const string Slice13Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            const MAX: u32 = 100;
            const MIN: u32 = 10;
            const RANGE: u32 = MAX - MIN;
            const FLAG: bool = true;

            fun check_range(v: u32) -> u32 {
                if (v < MIN) { return 1; }
                if (v > MAX) { return 2; }
                return 0;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var x: u32 = MAX + 5;
                if (x != 105) { exit_process(1); }

                if (RANGE != 90) { exit_process(2); }

                if (not FLAG) { exit_process(3); }

                var r: u32 = check_range(50);
                if (r != 0) { exit_process(4); }

                var sum: u32 = 0;
                var i: u32 = 1;
                while (i <= MIN) {
                    sum = sum + i;
                    i = i + 1;
                }
                if (sum != 55) { exit_process(5); }

                exit_process(0);
            }
        }
        """;

    public const string Slice14CharLiteralsSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var a: byte = 'A';
                if (a != 65) { exit_process(1); }

                var nl: byte = '\n';
                if (nl != 10) { exit_process(2); }

                var tab: byte = '\t';
                if (tab != 9) { exit_process(3); }

                var bs: byte = '\\';
                if (bs != 92) { exit_process(4); }

                var nul: byte = '\0';
                if (nul != 0) { exit_process(5); }

                var sq: byte = '\'';
                if (sq != 39) { exit_process(6); }

                exit_process(0);
            }
        }
        """;

    public const string Slice14StringLenSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            const EMPTY: string = "";
            const HELLO: string = "hello";
            const MSG: string = "Hello, world!";
            const ESC: string = "a\nb";

            [win32_entry]
            [noreturn]
            fun main() -> () {
                if (EMPTY.len != 0) { exit_process(1); }
                if (HELLO.len != 5) { exit_process(2); }
                if (MSG.len != 13) { exit_process(3); }
                if (ESC.len != 3) { exit_process(4); }

                const WORD: string = "Pint";
                if (WORD.len != 4) { exit_process(5); }

                exit_process(0);
            }
        }
        """;

    public const string Slice14StringPtrSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            const MSG: string = "ABC";

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var p: ^byte = MSG.ptr;

                if (p^ != 'A') { exit_process(1); }

                p = p + 1;
                if (p^ != 'B') { exit_process(2); }

                p = p + 1;
                if (p^ != 'C') { exit_process(3); }

                // null terminator at ptr + len
                p = p + 1;
                if (p^ != 0) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    // STD_OUTPUT_HANDLE = (DWORD)(-11) = 0xFFFFFFF5
    public const string Slice14StringWriteSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [dll_import(dll="kernel32.dll", entry_point="GetStdHandle")]
            extern fun get_std_handle(nStdHandle: u32) -> u32;

            [dll_import(dll="kernel32.dll", entry_point="WriteFile")]
            extern fun write_file(hFile: u32, lpBuffer: ^byte, nNumberOfBytesToWrite: u32, lpNumberOfBytesWritten: ^u32, lpOverlapped: u32) -> u32;

            const STD_OUTPUT_HANDLE: u32 = 0xFFFFFFF5;
            const GREETING: string = "Hello, Pint!";

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var handle: u32 = get_std_handle(STD_OUTPUT_HANDLE);
                var written: u32 = 0;
                write_file(handle, GREETING.ptr, GREETING.len, @written, 0);
                exit_process(0);
            }
        }
        """;

    public const string Slice15Source = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun divrem(a: u32, b: u32) -> (u32, u32) {
                return a / b, a % b;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var (q: u32, r: u32) = divrem(10, 3);
                if (q != 3) { exit_process(1); }
                if (r != 1) { exit_process(2); }

                var (q2: u32, r2: u32) = divrem(7, 2);
                if (q2 != 3) { exit_process(3); }
                if (r2 != 1) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice15DiscardSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun divrem(a: u32, b: u32) -> (u32, u32) {
                return a / b, a % b;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var (q: u32, _) = divrem(17, 5);
                if (q != 3) { exit_process(1); }

                var (_, r: u32) = divrem(17, 5);
                if (r != 2) { exit_process(2); }

                exit_process(0);
            }
        }
        """;

    public const string Slice15MultiAssignSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun minmax(a: u32, b: u32) -> (u32, u32) {
                if (a < b) {
                    return a, b;
                }
                return b, a;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var lo: u32;
                var hi: u32;
                (lo, hi) = minmax(7, 3);
                if (lo != 3) { exit_process(1); }
                if (hi != 7) { exit_process(2); }

                (lo, hi) = minmax(2, 9);
                if (lo != 2) { exit_process(3); }
                if (hi != 9) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice16CastSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var big: u32 = 300;
                var b8: u32 = cast(big, u8);
                if (b8 != 44) { exit_process(1); }

                var wide: u32 = cast(200, u32);
                if (wide != 200) { exit_process(2); }

                var x: u32 = 500;
                var t8: u32 = to_u8(x);
                if (t8 != 244) { exit_process(3); }

                var y: u32 = 70000;
                var t16: u32 = to_u16(y);
                if (t16 != 4464) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice16SizeofLengthSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var s1: u32 = sizeof(u8);
                if (s1 != 1) { exit_process(1); }

                var s2: u32 = sizeof(u32);
                if (s2 != 4) { exit_process(2); }

                var s3: u32 = sizeof([5]u32);
                if (s3 != 20) { exit_process(3); }

                var arr: [7]u32;
                var len: u32 = length(arr);
                if (len != 7) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice16DivmodSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var (q: u32, r: u32) = divmod(17, 5);
                if (q != 3) { exit_process(1); }
                if (r != 2) { exit_process(2); }

                var (q2: u32, r2: u32) = divmod(100, 7);
                if (q2 != 14) { exit_process(3); }
                if (r2 != 2) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice16MulSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var (lo: u32, hi: u32) = mul(6, 7);
                if (lo != 42) { exit_process(1); }
                if (hi != 0) { exit_process(2); }

                var (lo2: u32, hi2: u32) = mul(2147483648, 3);
                if (lo2 != 2147483648) { exit_process(3); }
                if (hi2 != 1) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice17NamedArgsSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun subtract(minuend: u32, subtrahend: u32) -> u32 {
                return minuend - subtrahend;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // named args in declared order
                var r1: u32 = subtract(minuend: 10, subtrahend: 3);
                if (r1 != 7) { exit_process(1); }

                // named args out of declared order — key order-independence check
                var r2: u32 = subtract(subtrahend: 3, minuend: 10);
                if (r2 != 7) { exit_process(2); }

                exit_process(0);
            }
        }
        """;

    public const string Slice18NamedReturnSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun divrem(a: u32, b: u32) -> (quot: u32, rem: u32) {
                return quot: a / b, rem: a % b;
            }

            fun divrem_rev(a: u32, b: u32) -> (quot: u32, rem: u32) {
                // named return out of declared order — key order-independence check
                return rem: a % b, quot: a / b;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var (q: u32, r: u32) = divrem(10, 3);
                if (q != 3) { exit_process(1); }
                if (r != 1) { exit_process(2); }

                var (q2: u32, r2: u32) = divrem_rev(7, 2);
                if (q2 != 3) { exit_process(3); }
                if (r2 != 1) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    public const string Slice18NamedUnpackSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun divrem(a: u32, b: u32) -> (quot: u32, rem: u32) {
                return quot: a / b, rem: a % b;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                // named unpack in declared order
                var q: u32;
                var r: u32;
                (quot: q, rem: r) = divrem(10, 3);
                if (q != 3) { exit_process(1); }
                if (r != 1) { exit_process(2); }

                // named unpack out of declared order — key order-independence check
                var q2: u32;
                var r2: u32;
                (rem: r2, quot: q2) = divrem(7, 2);
                if (q2 != 3) { exit_process(3); }
                if (r2 != 1) { exit_process(4); }

                exit_process(0);
            }
        }
        """;

    // Demonstrates the re-evaluation bug: const initializer is a call whose side
    // effect is visible through a pointer. With the bug the call runs once per use
    // (a=1, b=2). With the fix it runs once at the declaration (a=1, b=1).
    public const string Slice12ConstReevalSource = """
        module main {

            [dll_import(dll="kernel32.dll", entry_point="ExitProcess")]
            [noreturn]
            extern fun exit_process(code: u32) -> ();

            fun inc_and_get(p: ^u32) -> u32 {
                p^ = p^ + 1;
                return p^;
            }

            [win32_entry]
            [noreturn]
            fun main() -> () {
                var count: u32 = 0;
                const BASE: u32 = inc_and_get(@count);
                var a: u32 = BASE;
                var b: u32 = BASE;
                if (a != b) { exit_process(1); }
                exit_process(0);
            }
        }
        """;
}
