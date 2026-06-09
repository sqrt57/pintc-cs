using System.Text;

namespace Pintc;

// Assembles a minimal PE32 EXE from a CodeUnit.
// Output: DOS stub + COFF + Optional header + .text + .idata (single-DLL import).
// Fixed base address 0x00400000; no relocation table.
static class PeWriter
{
    const uint ImageBase = 0x00400000u;
    const uint SecAlign  = 0x1000u;
    const uint FileAlign = 0x0200u;
    const uint HdrSize   = 0x0200u;  // SizeOfHeaders, file-aligned
    const uint TextRva   = 0x1000u;
    const uint DataRva   = 0x2000u;  // .data RVA when present; .idata shifts to 0x3000
    const uint IdataRvaNoData  = 0x2000u;  // .idata when no .data section
    const uint IdataRvaWithData = 0x3000u; // .idata when .data section present

    // Minimal IMAGE_DOS_HEADER (64 bytes). e_lfanew = 0x40: PE header follows immediately.
    static readonly byte[] DosHeader = [
        0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00,  // e_magic, e_cblp, e_cp, e_crlc
        0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,  // e_cparhdr, e_minalloc, e_maxalloc, e_ss
        0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // e_sp, e_csum, e_ip, e_cs
        0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // e_lfarlc, e_ovno, e_res[0..1]
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // e_res[2..5]
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // e_oemid, e_oeminfo, e_res2[0..2]
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,  // e_res2[3..6]
        0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00,  // e_res2[7..9], e_lfanew
    ];

    public static void Write(CodeUnit unit, Stream output)
    {
        bool hasData = unit.Data.Length > 0;
        uint idataRva = hasData ? IdataRvaWithData : IdataRvaNoData;

        var idataBlob = BuildIdata(unit.Imports, idataRva, out var iatVas, out var idtSize);

        // Patch IAT addresses into a copy of the code.
        var code = (byte[])unit.Code.Clone();
        foreach (var rel in unit.IatRefs)
            BitConverter.TryWriteBytes(code.AsSpan(rel.CodeOffset, 4), iatVas[rel.Import]);

        uint codeVirtSize  = (uint)code.Length;
        uint dataVirtSize  = (uint)unit.Data.Length;
        uint idataVirtSize = (uint)idataBlob.Length;
        uint codeRawSize   = AlignUp(codeVirtSize,  FileAlign);
        uint dataRawSize   = hasData ? AlignUp(dataVirtSize, FileAlign) : 0u;
        uint idataRawSize  = AlignUp(idataVirtSize, FileAlign);
        uint textFileOff   = HdrSize;
        uint dataFileOff   = textFileOff + codeRawSize;
        uint idataFileOff  = hasData ? dataFileOff + dataRawSize : textFileOff + codeRawSize;
        uint sizeOfImage   = AlignUp(idataRva + idataVirtSize, SecAlign);
        uint sizeOfInitData = (hasData ? dataRawSize : 0u) + idataRawSize;
        ushort numSections = hasData ? (ushort)3 : (ushort)2;

        using var bw = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);

        // ── DOS header ───────────────────────────────────────────
        bw.Write(DosHeader);

        // ── PE signature ─────────────────────────────────────────
        bw.Write(0x00004550u); // "PE\0\0"

        // ── COFF header (20 bytes) ───────────────────────────────
        bw.Write((ushort)0x014C); // Machine: I386
        bw.Write(numSections);    // NumberOfSections
        bw.Write(0u);             // TimeDateStamp
        bw.Write(0u);             // PointerToSymbolTable
        bw.Write(0u);             // NumberOfSymbols
        bw.Write((ushort)0x00E0); // SizeOfOptionalHeader: 224
        bw.Write((ushort)0x0102); // Characteristics: EXECUTABLE_IMAGE | 32BIT_MACHINE

        // ── Optional header — standard fields (28 bytes) ─────────
        bw.Write((ushort)0x010B); // Magic: PE32
        bw.Write((byte)0);        // MajorLinkerVersion
        bw.Write((byte)0);        // MinorLinkerVersion
        bw.Write(codeRawSize);    // SizeOfCode
        bw.Write(sizeOfInitData); // SizeOfInitializedData
        bw.Write(0u);             // SizeOfUninitializedData
        bw.Write(TextRva);        // AddressOfEntryPoint
        bw.Write(TextRva);        // BaseOfCode
        bw.Write(0x2000u);        // BaseOfData (PE32 only) — always 0x2000

        // ── Optional header — Windows-specific fields (68 bytes) ─
        bw.Write(ImageBase);      // ImageBase
        bw.Write(SecAlign);       // SectionAlignment
        bw.Write(FileAlign);      // FileAlignment
        bw.Write((ushort)4);      // MajorOperatingSystemVersion
        bw.Write((ushort)0);      // MinorOperatingSystemVersion
        bw.Write((ushort)0);      // MajorImageVersion
        bw.Write((ushort)0);      // MinorImageVersion
        bw.Write((ushort)4);      // MajorSubsystemVersion
        bw.Write((ushort)0);      // MinorSubsystemVersion
        bw.Write(0u);             // Win32VersionValue
        bw.Write(sizeOfImage);    // SizeOfImage
        bw.Write(HdrSize);        // SizeOfHeaders
        bw.Write(0u);             // CheckSum
        bw.Write((ushort)2);      // Subsystem: WINDOWS_GUI
        bw.Write((ushort)0);      // DllCharacteristics
        bw.Write(0x00100000u);    // SizeOfStackReserve
        bw.Write(0x00001000u);    // SizeOfStackCommit
        bw.Write(0x00100000u);    // SizeOfHeapReserve
        bw.Write(0x00001000u);    // SizeOfHeapCommit
        bw.Write(0u);             // LoaderFlags
        bw.Write(16u);            // NumberOfRvaAndSizes

        // ── Data directories (16 × 8 bytes = 128 bytes) ──────────
        bw.Write(0u); bw.Write(0u);   // [0] Export: empty
        bw.Write(idataRva);           // [1] Import: RVA
        bw.Write(idtSize);            // [1] Import: Size (IDT only, not full .idata)
        for (int i = 2; i < 16; i++) { bw.Write(0u); bw.Write(0u); }

        // ── Section table ─────────────────────────────────────────
        // .text
        bw.Write(Encoding.Latin1.GetBytes(".text\0\0\0")); // Name (8 bytes)
        bw.Write(codeVirtSize);   // VirtualSize
        bw.Write(TextRva);        // VirtualAddress
        bw.Write(codeRawSize);    // SizeOfRawData
        bw.Write(textFileOff);    // PointerToRawData
        bw.Write(0u);             // PointerToRelocations
        bw.Write(0u);             // PointerToLinenumbers
        bw.Write((ushort)0);      // NumberOfRelocations
        bw.Write((ushort)0);      // NumberOfLinenumbers
        bw.Write(0x60000020u);    // Characteristics: CNT_CODE | MEM_EXECUTE | MEM_READ

        // .data (only when vars are present)
        if (hasData)
        {
            bw.Write(Encoding.Latin1.GetBytes(".data\0\0\0")); // Name (8 bytes)
            bw.Write(dataVirtSize);   // VirtualSize
            bw.Write(DataRva);        // VirtualAddress
            bw.Write(dataRawSize);    // SizeOfRawData
            bw.Write(dataFileOff);    // PointerToRawData
            bw.Write(0u);             // PointerToRelocations
            bw.Write(0u);             // PointerToLinenumbers
            bw.Write((ushort)0);      // NumberOfRelocations
            bw.Write((ushort)0);      // NumberOfLinenumbers
            bw.Write(0xC0000040u);    // Characteristics: CNT_INITIALIZED_DATA | MEM_READ | MEM_WRITE
        }

        // .idata
        bw.Write(Encoding.Latin1.GetBytes(".idata\0\0")); // Name (8 bytes)
        bw.Write(idataVirtSize);  // VirtualSize
        bw.Write(idataRva);       // VirtualAddress
        bw.Write(idataRawSize);   // SizeOfRawData
        bw.Write(idataFileOff);   // PointerToRawData
        bw.Write(0u);             // PointerToRelocations
        bw.Write(0u);             // PointerToLinenumbers
        bw.Write((ushort)0);      // NumberOfRelocations
        bw.Write((ushort)0);      // NumberOfLinenumbers
        bw.Write(0xC0000040u);    // Characteristics: CNT_INITIALIZED_DATA | MEM_READ | MEM_WRITE

        // ── Pad headers to HdrSize ────────────────────────────────
        PadTo(bw, HdrSize);

        // ── .text raw data ────────────────────────────────────────
        bw.Write(code);
        PadTo(bw, textFileOff + codeRawSize);

        // ── .data raw data ────────────────────────────────────────
        if (hasData)
        {
            bw.Write(unit.Data);
            PadTo(bw, dataFileOff + dataRawSize);
        }

        // ── .idata raw data ───────────────────────────────────────
        bw.Write(idataBlob);
        PadTo(bw, idataFileOff + idataRawSize);
    }

    // Builds the .idata blob and returns:
    //   iatVas    — absolute VA for each ImportSpec's IAT slot (used to patch code)
    //   idtSize   — byte count of the IDT (for the Import data directory Size field)
    static byte[] BuildIdata(
        IReadOnlyList<ImportSpec> imports,
        uint idataRva,
        out Dictionary<ImportSpec, uint> iatVas,
        out uint idtSize)
    {
        var byDll = imports
            .GroupBy(i => i.DllName, StringComparer.OrdinalIgnoreCase)
            .Select(g => (DllName: g.Key, Funcs: g.ToList()))
            .ToList();
        int n = byDll.Count;

        idtSize = (uint)(n + 1) * 20;

        // ── Layout pass: compute every offset within the blob ─────
        int pos = 0;

        // IDT: (n + 1) descriptors × 20 bytes, including null terminator
        int idtPos = pos;
        pos += (n + 1) * 20;

        // ILT: one array per DLL, each (funcCount + 1) × 4 bytes
        var iltPos = new int[n];
        for (int d = 0; d < n; d++)
        {
            iltPos[d] = pos;
            pos += (byDll[d].Funcs.Count + 1) * 4;
        }

        // IAT: same shape as ILT; loader overwrites these with real function addresses
        var iatPos = new int[n];
        var funcIatPos = new Dictionary<ImportSpec, int>();
        for (int d = 0; d < n; d++)
        {
            iatPos[d] = pos;
            foreach (var f in byDll[d].Funcs)
            {
                funcIatPos[f] = pos;
                pos += 4;
            }
            pos += 4; // null terminator
        }

        // Hint/Name table: WORD-aligned hint (0) + null-terminated function name
        var funcHnPos = new Dictionary<ImportSpec, int>();
        for (int d = 0; d < n; d++)
        {
            foreach (var f in byDll[d].Funcs)
            {
                if (pos % 2 != 0) pos++;
                funcHnPos[f] = pos;
                pos += 2 + f.EntryPoint.Length + 1; // hint + name + null
            }
        }

        // DLL name strings
        var dllNamePos = new int[n];
        for (int d = 0; d < n; d++)
        {
            dllNamePos[d] = pos;
            pos += byDll[d].DllName.Length + 1;
        }
        if (pos % 2 != 0) pos++;

        // ── Emit pass ─────────────────────────────────────────────
        var blob = new byte[pos];
        using var ms = new MemoryStream(blob);
        using var bw = new BinaryWriter(ms, Encoding.Latin1, leaveOpen: true);

        // IDT
        for (int d = 0; d < n; d++)
        {
            bw.Write((uint)(idataRva + iltPos[d]));     // OriginalFirstThunk
            bw.Write(0u);                                // TimeDateStamp
            bw.Write(0u);                                // ForwarderChain
            bw.Write((uint)(idataRva + dllNamePos[d])); // Name
            bw.Write((uint)(idataRva + iatPos[d]));     // FirstThunk (IAT RVA)
        }
        for (int i = 0; i < 20; i++) bw.Write((byte)0); // null IDT terminator

        // ILTs
        for (int d = 0; d < n; d++)
        {
            foreach (var f in byDll[d].Funcs)
                bw.Write((uint)(idataRva + funcHnPos[f]));
            bw.Write(0u);
        }

        // IATs (mirror of ILTs; loader patches at load time)
        for (int d = 0; d < n; d++)
        {
            foreach (var f in byDll[d].Funcs)
                bw.Write((uint)(idataRva + funcHnPos[f]));
            bw.Write(0u);
        }

        // Hint/Name entries
        for (int d = 0; d < n; d++)
        {
            foreach (var f in byDll[d].Funcs)
            {
                PadTo(bw, (uint)funcHnPos[f]);
                bw.Write((ushort)0); // hint (0 = use name)
                bw.Write(Encoding.Latin1.GetBytes(f.EntryPoint));
                bw.Write((byte)0);
            }
        }

        // DLL name strings
        for (int d = 0; d < n; d++)
        {
            PadTo(bw, (uint)dllNamePos[d]);
            bw.Write(Encoding.Latin1.GetBytes(byDll[d].DllName));
            bw.Write((byte)0);
        }

        iatVas = [];
        foreach (var (spec, offset) in funcIatPos)
            iatVas[spec] = ImageBase + idataRva + (uint)offset;

        return blob;
    }

    public static void WriteDll(CodeUnit unit, string dllName, Stream output)
    {
        bool hasData    = unit.Data.Length > 0;
        bool hasImports = unit.Imports.Count > 0;

        const uint EdataRva = 0x2000u;
        uint dataRva        = EdataRva  + SecAlign;                    // 0x3000
        uint idataRva       = hasData ? dataRva + SecAlign : dataRva;  // 0x3000 or 0x4000

        var edataBlob = BuildEdata(unit.ExportedFuns, dllName, EdataRva);

        byte[]                       idataBlob = [];
        Dictionary<ImportSpec, uint> iatVas    = [];
        uint                         idtSize   = 0;
        if (hasImports)
            idataBlob = BuildIdata(unit.Imports, idataRva, out iatVas, out idtSize);

        var code = (byte[])unit.Code.Clone();
        foreach (var rel in unit.IatRefs)
            BitConverter.TryWriteBytes(code.AsSpan(rel.CodeOffset, 4), iatVas[rel.Import]);

        uint codeVirtSize  = (uint)code.Length;
        uint edataVirtSize = (uint)edataBlob.Length;
        uint dataVirtSize  = (uint)unit.Data.Length;
        uint idataVirtSize = (uint)idataBlob.Length;

        uint codeRawSize   = AlignUp(codeVirtSize,  FileAlign);
        uint edataRawSize  = AlignUp(edataVirtSize, FileAlign);
        uint dataRawSize   = hasData    ? AlignUp(dataVirtSize,  FileAlign) : 0u;
        uint idataRawSize  = hasImports ? AlignUp(idataVirtSize, FileAlign) : 0u;

        uint textFileOff   = HdrSize;
        uint edataFileOff  = textFileOff + codeRawSize;
        uint dataFileOff   = edataFileOff + edataRawSize;
        uint idataFileOff  = hasData ? dataFileOff + dataRawSize : edataFileOff + edataRawSize;

        uint lastRva   = hasImports ? idataRva  : hasData ? dataRva   : EdataRva;
        uint lastSize  = hasImports ? idataVirtSize : hasData ? dataVirtSize : edataVirtSize;
        uint sizeOfImage   = AlignUp(lastRva + lastSize, SecAlign);
        uint sizeOfInitData = edataRawSize + dataRawSize + idataRawSize;
        ushort numSections = (ushort)(2 + (hasData ? 1 : 0) + (hasImports ? 1 : 0));

        using var bw = new BinaryWriter(output, Encoding.Latin1, leaveOpen: true);

        bw.Write(DosHeader);
        bw.Write(0x00004550u);

        // COFF header
        bw.Write((ushort)0x014C);   // Machine: I386
        bw.Write(numSections);
        bw.Write(0u);               // TimeDateStamp
        bw.Write(0u);               // PointerToSymbolTable
        bw.Write(0u);               // NumberOfSymbols
        bw.Write((ushort)0x00E0);   // SizeOfOptionalHeader
        bw.Write((ushort)0x2102);   // EXECUTABLE_IMAGE | 32BIT_MACHINE | DLL

        // Optional header — standard fields
        bw.Write((ushort)0x010B);   // Magic: PE32
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write(codeRawSize);
        bw.Write(sizeOfInitData);
        bw.Write(0u);               // SizeOfUninitializedData
        bw.Write(0u);               // AddressOfEntryPoint: 0 = no DllMain
        bw.Write(TextRva);          // BaseOfCode
        bw.Write(EdataRva);         // BaseOfData

        // Optional header — Windows-specific fields
        bw.Write(ImageBase);
        bw.Write(SecAlign);
        bw.Write(FileAlign);
        bw.Write((ushort)4);        // MajorOperatingSystemVersion
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)0);
        bw.Write((ushort)4);        // MajorSubsystemVersion
        bw.Write((ushort)0);
        bw.Write(0u);               // Win32VersionValue
        bw.Write(sizeOfImage);
        bw.Write(HdrSize);
        bw.Write(0u);               // CheckSum
        bw.Write((ushort)2);        // Subsystem: WINDOWS_GUI
        bw.Write((ushort)0);        // DllCharacteristics
        bw.Write(0x00100000u);
        bw.Write(0x00001000u);
        bw.Write(0x00100000u);
        bw.Write(0x00001000u);
        bw.Write(0u);               // LoaderFlags
        bw.Write(16u);

        // Data directories
        bw.Write(EdataRva);         // [0] Export RVA
        bw.Write(edataVirtSize);    // [0] Export Size
        if (hasImports) { bw.Write(idataRva); bw.Write(idtSize); }
        else            { bw.Write(0u);       bw.Write(0u);      }
        for (int i = 2; i < 16; i++) { bw.Write(0u); bw.Write(0u); }

        // Section table
        WriteSectionHeader(bw, ".text\0\0\0",  codeVirtSize,  TextRva,   codeRawSize,  textFileOff,  0x60000020u);
        WriteSectionHeader(bw, ".edata\0\0",   edataVirtSize, EdataRva,  edataRawSize, edataFileOff, 0x40000040u);
        if (hasData)
            WriteSectionHeader(bw, ".data\0\0\0",  dataVirtSize,  dataRva,  dataRawSize,  dataFileOff,  0xC0000040u);
        if (hasImports)
            WriteSectionHeader(bw, ".idata\0\0",   idataVirtSize, idataRva, idataRawSize, idataFileOff, 0xC0000040u);

        PadTo(bw, HdrSize);

        bw.Write(code);
        PadTo(bw, textFileOff + codeRawSize);

        bw.Write(edataBlob);
        PadTo(bw, edataFileOff + edataRawSize);

        if (hasData)
        {
            bw.Write(unit.Data);
            PadTo(bw, dataFileOff + dataRawSize);
        }
        if (hasImports)
        {
            bw.Write(idataBlob);
            PadTo(bw, idataFileOff + idataRawSize);
        }
    }

    // Builds the .edata (export directory) blob.
    static byte[] BuildEdata(IReadOnlyList<ExportedFun> exports, string dllName, uint edataRva)
    {
        // Sort by name: PE spec requires name pointer table to be sorted for binary search
        var sorted = exports
            .Select((f, i) => (f, declIdx: i))
            .OrderBy(x => x.f.Name, StringComparer.Ordinal)
            .ToList();
        int n = exports.Count;

        // Layout within blob:
        // [0]    IMAGE_EXPORT_DIRECTORY (40 bytes)
        // [40]   Address table:  n × DWORD  (function RVAs, in declaration order = ordinal order)
        // [40+n*4]  Name pointer table: n × DWORD  (name string RVAs, in sorted order)
        // [40+n*8]  Ordinal table:      n × WORD   (0-based indices into address table)
        // [...]  DLL name string + function name strings
        int addrTableOff    = 40;
        int nameTableOff    = addrTableOff    + n * 4;
        int ordinalTableOff = nameTableOff    + n * 4;
        int stringAreaOff   = ordinalTableOff + n * 2;
        if (stringAreaOff % 2 != 0) stringAreaOff++;

        int dllNameOff  = stringAreaOff;
        int funcNamesOff = dllNameOff + dllName.Length + 1;

        var funcNamePos = new int[n];
        int pos = funcNamesOff;
        for (int i = 0; i < n; i++)
        {
            funcNamePos[i] = pos;
            pos += sorted[i].f.Name.Length + 1;
        }

        var blob = new byte[pos];
        using var ms = new MemoryStream(blob);
        using var bw = new BinaryWriter(ms, Encoding.Latin1, leaveOpen: true);

        // IMAGE_EXPORT_DIRECTORY
        bw.Write(0u);                                           // Characteristics
        bw.Write(0u);                                           // TimeDateStamp
        bw.Write((ushort)0);                                    // MajorVersion
        bw.Write((ushort)0);                                    // MinorVersion
        bw.Write((uint)(edataRva + dllNameOff));                // Name
        bw.Write(1u);                                           // Base (ordinal base)
        bw.Write((uint)n);                                      // NumberOfFunctions
        bw.Write((uint)n);                                      // NumberOfNames
        bw.Write((uint)(edataRva + addrTableOff));              // AddressOfFunctions
        bw.Write((uint)(edataRva + nameTableOff));              // AddressOfNames
        bw.Write((uint)(edataRva + ordinalTableOff));           // AddressOfNameOrdinals

        // Address table: function RVAs in declaration (ordinal) order
        foreach (var f in exports)
            bw.Write(TextRva + (uint)f.CodeOffset);

        // Name pointer table: name RVAs in sorted order
        for (int i = 0; i < n; i++)
            bw.Write((uint)(edataRva + funcNamePos[i]));

        // Ordinal table: sorted[i].declIdx = 0-based index into address table
        for (int i = 0; i < n; i++)
            bw.Write((ushort)sorted[i].declIdx);

        // DLL name string
        ms.Position = dllNameOff;
        bw.Write(Encoding.Latin1.GetBytes(dllName));
        bw.Write((byte)0);

        // Function name strings (in sorted order)
        for (int i = 0; i < n; i++)
        {
            ms.Position = funcNamePos[i];
            bw.Write(Encoding.Latin1.GetBytes(sorted[i].f.Name));
            bw.Write((byte)0);
        }

        return blob;
    }

    static void WriteSectionHeader(BinaryWriter bw, string name, uint virtSize, uint rva,
                                   uint rawSize, uint fileOff, uint characteristics)
    {
        bw.Write(Encoding.Latin1.GetBytes(name));
        bw.Write(virtSize);
        bw.Write(rva);
        bw.Write(rawSize);
        bw.Write(fileOff);
        bw.Write(0u);           // PointerToRelocations
        bw.Write(0u);           // PointerToLinenumbers
        bw.Write((ushort)0);    // NumberOfRelocations
        bw.Write((ushort)0);    // NumberOfLinenumbers
        bw.Write(characteristics);
    }

    static uint AlignUp(uint value, uint align) => (value + align - 1) / align * align;

    static void PadTo(BinaryWriter bw, uint target)
    {
        while (bw.BaseStream.Position < target) bw.Write((byte)0);
    }
}
