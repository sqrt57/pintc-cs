namespace Pintc;

// An import resolved to a specific DLL entry point.
record ImportSpec(string DllName, string EntryPoint);

// A site in Code where PeWriter must write the 4-byte absolute IAT VA.
record IatRef(int CodeOffset, ImportSpec Import);

// Output of codegen; input to PeWriter.
class CodeUnit
{
    public required byte[]          Code    { get; init; }
    public required List<IatRef>    IatRefs { get; init; }
    public required List<ImportSpec> Imports { get; init; }
}
