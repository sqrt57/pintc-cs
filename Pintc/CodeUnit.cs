namespace Pintc;

// An import resolved to a specific DLL entry point.
record ImportSpec(string DllName, string EntryPoint);

// A site in Code where PeWriter must write the 4-byte absolute IAT VA.
record IatRef(int CodeOffset, ImportSpec Import);

// A function exported from a DLL compilation unit.
record ExportedFun(string Name, int CodeOffset);

// Output of codegen; input to PeWriter.
class CodeUnit
{
    public required byte[]              Code         { get; init; }
    public required List<IatRef>        IatRefs      { get; init; }
    public required List<ImportSpec>    Imports      { get; init; }
    public required byte[]              Data         { get; init; }  // .data section bytes; empty = no .data section
    public required byte[]              ReadOnly     { get; init; }  // .rdata section; empty = no section
    public required List<ExportedFun>   ExportedFuns { get; init; }  // DLL exports; empty for EXE output
}
