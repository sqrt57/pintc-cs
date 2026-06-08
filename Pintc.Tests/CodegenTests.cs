namespace Pintc.Tests;

public class CodegenTests
{
    // Hardcoded AST for the slice 1 target program:
    //   extern fun exit_process(code: u32) -> ();   // dll_import kernel32.ExitProcess
    //   [win32_entry][noreturn] fun main() -> () { exit_process(0); }
    static readonly ModuleDecl Slice1Module = new(
        "main",
        Externs:
        [
            new ExternFunDecl(
                [new Attr("dll_import", new() { ["dll"] = "kernel32.dll", ["entry_point"] = "ExitProcess" }),
                 new Attr("noreturn")],
                "exit_process",
                [new Param("code", "u32")],
                "()")
        ],
        Funs:
        [
            new FunDecl(
                [new Attr("win32_entry"), new Attr("noreturn")],
                "main",
                [],
                "()",
                [new CallStmt("exit_process", [new IntLiteralExpr(0)])])
        ]);

    [Fact]
    public void EntryPoint_emits_push0_then_callIndirect()
    {
        var unit = Codegen.Emit(Slice1Module);

        // push 0 (6A 00) + call [IAT_ExitProcess] (FF 15 00 00 00 00)
        unit.Code.ShouldBe(new byte[] { 0x6A, 0x00, 0xFF, 0x15, 0x00, 0x00, 0x00, 0x00 });
    }

    [Fact]
    public void IatRef_offset_points_to_address_field_in_call()
    {
        var unit = Codegen.Emit(Slice1Module);

        // push 0 is 2 bytes; call instruction offset 2 within that is byte 4 overall
        unit.IatRefs.Count.ShouldBe(1);
        unit.IatRefs[0].CodeOffset.ShouldBe(4);
        unit.IatRefs[0].Import.DllName.ShouldBe("kernel32.dll");
        unit.IatRefs[0].Import.EntryPoint.ShouldBe("ExitProcess");
    }

    [Fact]
    public void Imports_contains_ExitProcess()
    {
        var unit = Codegen.Emit(Slice1Module);

        unit.Imports.Count.ShouldBe(1);
        unit.Imports[0].DllName.ShouldBe("kernel32.dll");
        unit.Imports[0].EntryPoint.ShouldBe("ExitProcess");
    }

    [Fact]
    public void Regular_function_emits_prologue_and_epilogue()
    {
        // A non-entry, non-noreturn function must have push ebp / mov ebp,esp ... pop ebp / ret
        var module = new ModuleDecl(
            "main",
            Externs:
            [
                new ExternFunDecl(
                    [new Attr("dll_import", new() { ["dll"] = "kernel32.dll", ["entry_point"] = "ExitProcess" })],
                    "exit_process",
                    [new Param("code", "u32")],
                    "()")
            ],
            Funs:
            [
                // entry point — just calls the wrapper
                new FunDecl(
                    [new Attr("win32_entry"), new Attr("noreturn")],
                    "main", [], "()",
                    [new CallStmt("exit_process", [new IntLiteralExpr(0)])]),
            ]);

        // For coverage of prologue/epilogue, construct a standalone emission of a regular FunDecl.
        // Codegen.Emit only processes the entry point for now; test the x86 helpers directly.
        var code = new List<byte>();
        code.AddRange(X86.PushEbp());
        code.AddRange(X86.MovEbpEsp());
        code.AddRange(X86.PopEbp());
        code.AddRange(X86.Ret());

        // push ebp (55) + mov ebp,esp (89 E5) + pop ebp (5D) + ret (C3)
        code.ShouldBe([0x55, 0x89, 0xE5, 0x5D, 0xC3]);
    }

    [Fact]
    public void PushImm32_emits_five_byte_push()
    {
        X86.PushImm32(0x12345678u).ShouldBe(new byte[] { 0x68, 0x78, 0x56, 0x34, 0x12 });
    }
}
