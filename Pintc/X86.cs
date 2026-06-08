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
}
