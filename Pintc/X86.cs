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

    // call rel32 — direct near call; displacement field starts at byte offset 1.
    public static byte[] CallRel32() => [0xE8, 0x00, 0x00, 0x00, 0x00];
    public const int CallRel32DispAt = 1;

    // add esp, imm8 — caller stack cleanup after a cdecl call
    public static byte[] AddEspImm8(byte bytes) => [0x83, 0xC4, bytes];

    // sub esp, imm8 — allocates bytes on the stack for local variables (fits in 7 bits)
    public static byte[] SubEspImm8(byte bytes) => [0x83, 0xEC, bytes];

    // push dword ptr [ebp+disp8] — loads a local variable onto the stack
    public static byte[] PushEbpDisp8(sbyte disp) => [0xFF, 0x75, (byte)disp];

    // pop dword ptr [ebp+disp8] — stores TOS into a local variable slot
    public static byte[] PopToEbpDisp8(sbyte disp) => [0x8F, 0x45, (byte)disp];

    // mov eax, [ebp + ecx*4 + disp8] — loads array element (base in EBP, index in ECX, scale 4)
    public static byte[] MovEaxEbpEcx4Disp8(sbyte disp) => [0x8B, 0x44, 0x8D, (byte)disp];

    // mov [ebp + ecx*4 + disp8], eax — stores EAX into array element
    public static byte[] MovEbpEcx4Disp8Eax(sbyte disp) => [0x89, 0x44, 0x8D, (byte)disp];

    // lea eax, [ebp + disp8] — address of a local variable slot
    public static byte[] LeaEaxEbpDisp8(sbyte disp) => [0x8D, 0x45, (byte)disp];

    // lea eax, [ebp + ecx*4 + disp8] — address of an array element
    public static byte[] LeaEaxEbpEcx4Disp8(sbyte disp) => [0x8D, 0x44, 0x8D, (byte)disp];

    // mov eax, [eax] — dereference: load 32-bit value at address in EAX
    public static byte[] MovEaxMemEax() => [0x8B, 0x00];

    // mov al, [eax] — load byte from address in EAX into AL
    public static byte[] MovAlMemEax() => [0x8A, 0x00];

    // mov eax, [eax + disp8] — load from pointer + byte offset (field access through pointer)
    public static byte[] MovEaxMemEaxDisp8(sbyte disp) => [0x8B, 0x40, (byte)disp];

    // mov [ecx], eax — store EAX at address in ECX (dereference write, zero offset)
    public static byte[] MovMemEcxEax() => [0x89, 0x01];

    // mov [ecx + disp8], eax — store EAX at pointer + byte offset (arrow-assign)
    public static byte[] MovMemEcxDisp8Eax(sbyte disp) => [0x89, 0x41, (byte)disp];

    // imul ecx, ecx, imm8 — multiply ECX by an immediate byte (pointer stride scaling)
    public static byte[] ImulEcxImm8(byte imm) => [0x6B, 0xC9, imm];

    // leave — mov esp,ebp; pop ebp (standard frame teardown)
    public static byte[] Leave() => [0xC9];

    // pop eax / pop ecx — pop TOS into a register
    public static byte[] PopEax() => [0x58];
    public static byte[] PopEcx() => [0x59];

    // push eax / push edx — push a register onto the stack
    public static byte[] PushEax() => [0x50];
    public static byte[] PushEdx() => [0x52];

    // lea eax, [esp] — address of the current stack top (used to capture hidden-pointer address)
    public static byte[] LeaEaxEsp() => [0x8D, 0x04, 0x24];

    // mov ecx, [ebp+disp8] — load a frame slot into ECX (e.g. the hidden return pointer)
    public static byte[] MovEcxEbpDisp8(sbyte disp) => [0x8B, 0x4D, (byte)disp];

    // mov eax, [esp+disp8] — stack-relative load (reload saved hidden pointer after arg pushes)
    public static byte[] MovEaxEspDisp8(byte disp) => [0x8B, 0x44, 0x24, disp];

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

    // and eax, imm32 — mask EAX to a fixed bit-width (used by cast/to_u8/to_u16)
    public static byte[] AndEaxImm32(uint mask) =>
        [0x25, (byte)(mask & 0xFF), (byte)(mask >> 8 & 0xFF), (byte)(mask >> 16 & 0xFF), (byte)(mask >> 24)];

    // movsx eax, al  — sign-extend AL into EAX (to_i8)
    public static byte[] MovsxEaxAl() => [0x0F, 0xBE, 0xC0];

    // movsx eax, ax  — sign-extend AX into EAX (to_i16)
    public static byte[] MovsxEaxAx() => [0x0F, 0xBF, 0xC0];

    // mul ecx — unsigned wide multiply: EDX:EAX = EAX * ECX (mul builtin)
    public static byte[] MulEcx() => [0xF7, 0xE1];

    // mov [ebp+disp8], eax — store EAX into a frame slot (inline builtin results)
    public static byte[] MovEbpDisp8Eax(sbyte disp) => [0x89, 0x45, (byte)disp];

    // mov [ebp+disp8], edx — store EDX into a frame slot (inline builtin results)
    public static byte[] MovEbpDisp8Edx(sbyte disp) => [0x89, 0x55, (byte)disp];

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
