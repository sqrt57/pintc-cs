namespace Pintc;

// x86 (IA-32) instruction encoding helpers.
// Each method returns raw instruction bytes; callers build sequences by concatenation.
static class X86
{
    // push imm8 — sign-extended to 32 bits on the stack
    public static byte[] PushImm8(byte value) => [0x6A, value];

    // call dword ptr [abs32] — indirect call through an IAT slot.
    // The 4-byte abs32 field at byte offset CallIndirectMemAddressOffset is zeroed;
    // PeWriter patches it to the runtime IAT VA via the corresponding IatRef.
    public static byte[] CallIndirectMem() => [0xFF, 0x15, 0x00, 0x00, 0x00, 0x00];
    public const int CallIndirectMemAddressOffset = 2;

    // push imm32 — pushes a sign-extended 32-bit immediate value
    public static byte[] PushImm32(uint value) =>
        [0x68, (byte)(value & 0xFF), (byte)(value >> 8 & 0xFF), (byte)(value >> 16 & 0xFF), (byte)(value >> 24)];

    // push dword ptr [abs32] — pushes the 32-bit value stored at an absolute memory address
    public static byte[] PushMem32(uint address) =>
        [0xFF, 0x35, (byte)(address & 0xFF), (byte)(address >> 8 & 0xFF), (byte)(address >> 16 & 0xFF), (byte)(address >> 24)];

    // push ebp — saves caller's frame pointer
    public static byte[] PushEbp() => [0x55];

    // mov ebp, esp — sets up the new frame pointer
    public static byte[] MovEbpEsp() => [0x89, 0xE5];

    // pop ebp — restores caller's frame pointer
    public static byte[] PopEbp() => [0x5D];

    // ret — near return, callee pops nothing
    public static byte[] Ret() => [0xC3];

    // ret imm16 — near return, callee pops popBytes bytes (stdcall cleanup)
    public static byte[] RetN(ushort popBytes) =>
        [0xC2, (byte)(popBytes & 0xFF), (byte)(popBytes >> 8)];

    // sub esp, imm8 — allocates bytes on the stack for local variables (fits in 7 bits)
    public static byte[] SubEspImm8(byte bytes) => [0x83, 0xEC, bytes];

    // push dword ptr [ebp+disp8] — loads a local variable onto the stack
    public static byte[] PushEbpDisp8(sbyte disp) => [0xFF, 0x75, (byte)disp];

    // pop dword ptr [ebp+disp8] — stores TOS into a local variable slot
    public static byte[] PopToEbpDisp8(sbyte disp) => [0x8F, 0x45, (byte)disp];

    // leave — mov esp,ebp; pop ebp (standard frame teardown)
    public static byte[] Leave() => [0xC9];

    // pop eax / pop ecx — pop TOS into a register
    public static byte[] PopEax() => [0x58];
    public static byte[] PopEcx() => [0x59];

    // push eax / push edx — push a register onto the stack
    public static byte[] PushEax() => [0x50];
    public static byte[] PushEdx() => [0x52];

    // Integer arithmetic (operands pre-loaded: left→EAX, right→ECX)
    public static byte[] AddEaxEcx()  => [0x03, 0xC1];
    public static byte[] SubEaxEcx()  => [0x2B, 0xC1];
    public static byte[] ImulEaxEcx() => [0x0F, 0xAF, 0xC1];
    public static byte[] XorEdxEdx()  => [0x33, 0xD2]; // zero EDX before div
    public static byte[] DivEcx()     => [0xF7, 0xF1]; // unsigned: EAX=quotient, EDX=remainder
    public static byte[] NegEax()     => [0xF7, 0xD8]; // two's complement negation
    public static byte[] NotEax()     => [0xF7, 0xD0]; // bitwise complement

    // Bitwise (operands pre-loaded: left→EAX, right→ECX)
    public static byte[] AndEaxEcx() => [0x23, 0xC1];
    public static byte[] OrEaxEcx()  => [0x0B, 0xC1];
    public static byte[] XorEaxEcx() => [0x33, 0xC1];

    // Shifts (value in EAX, count in CL)
    public static byte[] ShlEaxCl() => [0xD3, 0xE0]; // logical left
    public static byte[] ShrEaxCl() => [0xD3, 0xE8]; // logical right (unsigned)
    public static byte[] SarEaxCl() => [0xD3, 0xF8]; // arithmetic right (signed)

    // Comparison and condition codes (operands pre-loaded: left→EAX, right→ECX)
    public static byte[] CmpEaxEcx()  => [0x3B, 0xC1];
    public static byte[] SeteAl()     => [0x0F, 0x94, 0xC0]; // ZF=1
    public static byte[] SetneAl()    => [0x0F, 0x95, 0xC0]; // ZF=0
    public static byte[] SetbAl()     => [0x0F, 0x92, 0xC0]; // CF=1   (unsigned <)
    public static byte[] SetbeAl()    => [0x0F, 0x96, 0xC0]; // CF|ZF  (unsigned <=)
    public static byte[] SetaAl()     => [0x0F, 0x97, 0xC0]; // !CF&!ZF (unsigned >)
    public static byte[] SetaeAl()    => [0x0F, 0x93, 0xC0]; // !CF    (unsigned >=)
    public static byte[] MovzxEaxAl() => [0x0F, 0xB6, 0xC0]; // zero-extend AL into EAX

    // xor eax, 1 — inverts a bool (0→1, 1→0)
    public static byte[] XorEaxOne() => [0x83, 0xF0, 0x01];

    // test eax, eax — sets ZF=1 if EAX is zero, ZF=0 otherwise
    public static byte[] TestEaxEax() => [0x85, 0xC0];

    // jz rel32 — jump if ZF=1 (condition was false); placeholder offset at bytes [2..5]
    public static byte[] JzRel32() => [0x0F, 0x84, 0x00, 0x00, 0x00, 0x00];
    public const int JzRel32OffsetAt = 2;

    // jmp rel32 — unconditional jump; placeholder offset at bytes [1..4]
    public static byte[] JmpRel32() => [0xE9, 0x00, 0x00, 0x00, 0x00];
    public const int JmpRel32OffsetAt = 1;

    // Writes a 32-bit signed relative displacement into a previously emitted jump.
    // patchAt: byte index of the first byte of the 4-byte displacement field.
    // target: byte index in code that the jump should reach.
    // The CPU computes new_eip = (patchAt + 4) + displacement, so displacement = target - (patchAt + 4).
    public static void Backpatch(List<byte> code, int patchAt, int target)
    {
        int rel = target - (patchAt + 4);
        code[patchAt]     = (byte) rel;
        code[patchAt + 1] = (byte)(rel >> 8);
        code[patchAt + 2] = (byte)(rel >> 16);
        code[patchAt + 3] = (byte)(rel >> 24);
    }
}
