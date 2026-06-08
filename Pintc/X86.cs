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

    // ret — near return, callee pops nothing
    public static byte[] Ret() => [0xC3];

    // ret imm16 — near return, callee pops popBytes bytes (stdcall cleanup)
    public static byte[] RetN(ushort popBytes) =>
        [0xC2, (byte)(popBytes & 0xFF), (byte)(popBytes >> 8)];
}
