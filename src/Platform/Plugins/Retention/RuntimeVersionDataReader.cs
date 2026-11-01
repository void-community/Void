using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Reads a dump as the reader it wraps does, and reads the ELF modules of a Linux dump where that reader misses:
/// <list type="bullet">
///     <item>
///         ClrMD looks for the version of the runtime in the first writable segment of its image only, while the runtime keeps it in another
///         one, and without a version ClrMD takes the runtime for one of no version at all, matches no DAC to it, and reads its heap wrongly.
///     </item>
///     <item>
///         ClrMD reads the dynamic section of an image as glibc leaves it, with the addresses it points to, while musl leaves the offsets of
///         the image there, so ClrMD finds no exports, and no runtime in a single-file build, which the runtime's exports tell.
///     </item>
/// </list>
/// </summary>
internal sealed class RuntimeVersionDataReader(IDataReader reader) : IDataReader
{
    private const uint LoadableSegment = 1;
    private const uint DynamicSegment = 2;
    private const uint WritableSegment = 2;
    private const ushort SharedObject = 3;
    private const byte Elf32 = 1;
    private const byte Elf64 = 2;
    private const int Elf32HeaderSize = 52;
    private const int Elf64HeaderSize = 64;
    private const int Elf32ProgramHeaderSize = 32;
    private const int Elf64ProgramHeaderSize = 56;
    private const long StringTableTag = 5;
    private const long SymbolTableTag = 6;
    private const long SymbolEntrySizeTag = 11;
    private const int MaximumSymbols = 1 << 16;
    private const int MaximumSymbolNameLength = 256;

    private static readonly byte[] VersionMarker = "@(#)Version "u8.ToArray();

    public string DisplayName => reader.DisplayName;
    public bool IsThreadSafe => reader.IsThreadSafe;
    public OSPlatform TargetPlatform => reader.TargetPlatform;
    public Architecture Architecture => reader.Architecture;
    public int ProcessId => reader.ProcessId;
    public int PointerSize => reader.PointerSize;

    public IEnumerable<ModuleInfo> EnumerateModules()
    {
        return reader.EnumerateModules().Select(module => module.Kind is ModuleKind.Elf && ReadElfImage(module.ImageBase) is { } image ? new ElfModuleInfo(module, image, this) : module);
    }

    public void FlushCachedData()
    {
        reader.FlushCachedData();
    }

    public bool GetThreadContext(uint threadID, uint contextFlags, Span<byte> context)
    {
        return reader.GetThreadContext(threadID, contextFlags, context);
    }

    public int Read(ulong address, Span<byte> buffer)
    {
        return reader.Read(address, buffer);
    }

    public bool Read<T>(ulong address, out T value) where T : unmanaged
    {
        return reader.Read(address, out value);
    }

    public T Read<T>(ulong address) where T : unmanaged
    {
        return reader.Read<T>(address);
    }

    public bool ReadPointer(ulong address, out ulong value)
    {
        return reader.ReadPointer(address, out value);
    }

    public ulong ReadPointer(ulong address)
    {
        return reader.ReadPointer(address);
    }

    // 32-bit and 64-bit images place the same fields at different offsets.
    private ElfImage? ReadElfImage(ulong imageBase)
    {
        Span<byte> header = stackalloc byte[Elf64HeaderSize];
        var read = reader.Read(imageBase, header);

        if (read < Elf32HeaderSize || !header[..4].SequenceEqual("\u007fELF"u8) || header[4] is not (Elf32 or Elf64))
            return null;

        var is64Bit = header[4] is Elf64;

        if (is64Bit && read < Elf64HeaderSize)
            return null;

        var type = BinaryPrimitives.ReadUInt16LittleEndian(header[0x10..]);
        var programHeadersOffset = is64Bit ? BinaryPrimitives.ReadUInt64LittleEndian(header[0x20..]) : BinaryPrimitives.ReadUInt32LittleEndian(header[0x1C..]);
        var programHeaderEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(header[(is64Bit ? 0x36 : 0x2A)..]);
        var programHeaderCount = BinaryPrimitives.ReadUInt16LittleEndian(header[(is64Bit ? 0x38 : 0x2C)..]);
        var programHeaderSize = is64Bit ? Elf64ProgramHeaderSize : Elf32ProgramHeaderSize;
        Span<byte> programHeader = stackalloc byte[Elf64ProgramHeaderSize];
        var segments = new List<ElfSegment>(programHeaderCount);

        for (var index = 0; index < programHeaderCount; index++)
        {
            if (reader.Read(imageBase + programHeadersOffset + (ulong)(index * programHeaderEntrySize), programHeader[..programHeaderSize]) < programHeaderSize)
                return null;

            segments.Add(new ElfSegment(
                Type: BinaryPrimitives.ReadUInt32LittleEndian(programHeader),
                Flags: BinaryPrimitives.ReadUInt32LittleEndian(programHeader[(is64Bit ? 0x04 : 0x18)..]),
                VirtualAddress: is64Bit ? BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x10..]) : BinaryPrimitives.ReadUInt32LittleEndian(programHeader[0x08..]),
                MemorySize: is64Bit ? BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x28..]) : BinaryPrimitives.ReadUInt32LittleEndian(programHeader[0x14..])));
        }

        // An executable that isn't position independent keeps the addresses it's loaded at, rather than offsets from its base
        return new ElfImage(imageBase, is64Bit, type is SharedObject ? imageBase : 0, segments);
    }

    // Searches every writable segment that the image loads, as ClrMD does with the first one.
    private Version? FindElfVersion(ElfImage image)
    {
        foreach (var segment in image.Segments)
        {
            if (segment.Type is not LoadableSegment || (segment.Flags & WritableSegment) is 0)
                continue;

            if (FindVersion(image.LoadBias + segment.VirtualAddress, segment.MemorySize) is { } version)
                return version;
        }

        return null;
    }

    // Reads the symbols the image exports from its dynamic section, whose addresses glibc relocates and musl leaves as offsets of the image.
    private ulong FindElfExport(ElfImage image, string name)
    {
        if (image.Segments.FirstOrDefault(segment => segment.Type is DynamicSegment) is not { MemorySize: > 0 } dynamic)
            return 0;

        var entrySize = image.Is64Bit ? 16 : 8;
        var entries = new byte[dynamic.MemorySize];
        var read = reader.Read(image.LoadBias + dynamic.VirtualAddress, entries);
        ulong stringTable = 0, symbolTable = 0, symbolEntrySize = 0;

        for (var offset = 0; offset + entrySize <= read; offset += entrySize)
        {
            var entry = entries.AsSpan(offset, entrySize);
            var tag = image.Is64Bit ? BinaryPrimitives.ReadInt64LittleEndian(entry) : BinaryPrimitives.ReadInt32LittleEndian(entry);
            var value = image.Is64Bit ? BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]) : BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);

            if (tag is 0)
                break;

            if (tag is StringTableTag)
                stringTable = Relocate(image, value);
            else if (tag is SymbolTableTag)
                symbolTable = Relocate(image, value);
            else if (tag is SymbolEntrySizeTag)
                symbolEntrySize = value;
        }

        // The symbol table ends where the string table starts, as linkers lay them out
        if (stringTable is 0 || symbolTable is 0 || symbolEntrySize is 0 || stringTable <= symbolTable)
            return 0;

        var count = Math.Min((stringTable - symbolTable) / symbolEntrySize, MaximumSymbols);
        var expected = Encoding.ASCII.GetBytes(name + '\0');
        Span<byte> symbol = stackalloc byte[24];
        Span<byte> symbolName = stackalloc byte[Math.Min(expected.Length, MaximumSymbolNameLength)];

        for (ulong index = 1; index < count; index++)
        {
            var symbolSize = image.Is64Bit ? 24 : 16;

            if (reader.Read(symbolTable + index * symbolEntrySize, symbol[..symbolSize]) < symbolSize)
                return 0;

            var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(symbol);
            var value = image.Is64Bit ? BinaryPrimitives.ReadUInt64LittleEndian(symbol[8..]) : BinaryPrimitives.ReadUInt32LittleEndian(symbol[4..]);

            if (value is 0 || reader.Read(stringTable + nameOffset, symbolName) < symbolName.Length || !symbolName.SequenceEqual(expected))
                continue;

            return image.LoadBias + value;
        }

        return 0;
    }

    private static ulong Relocate(ElfImage image, ulong address)
    {
        return address < image.LoadBias ? image.LoadBias + address : address;
    }

    // A dump leaves out the pages of a segment that were never written, and a read across such a page fails, so it reads a page at a time.
    private Version? FindVersion(ulong start, ulong size)
    {
        var pageSize = (ulong)Environment.SystemPageSize;
        var page = new byte[pageSize + (ulong)VersionMarker.Length + 32];

        for (ulong offset = 0; offset < size; offset += pageSize)
        {
            var read = reader.Read(start + offset, page);

            if (read <= 0)
                continue;

            var index = page.AsSpan(0, read).IndexOf(VersionMarker);

            if (index < 0 || (ulong)index >= pageSize)
                continue;

            var text = Encoding.ASCII.GetString(page, index + VersionMarker.Length, read - index - VersionMarker.Length);
            var length = text.TakeWhile(character => char.IsAsciiDigit(character) || character is '.').Count();

            return Version.TryParse(text[..length], out var version) ? version : null;
        }

        return null;
    }

    private sealed record ElfImage(ulong ImageBase, bool Is64Bit, ulong LoadBias, IReadOnlyList<ElfSegment> Segments);

    private readonly record struct ElfSegment(uint Type, uint Flags, ulong VirtualAddress, ulong MemorySize);

    private sealed class ElfModuleInfo(ModuleInfo module, ElfImage image, RuntimeVersionDataReader reader) : ModuleInfo(module.ImageBase, module.FileName)
    {
        private readonly Lazy<Version> _version = new(() => module.Version != new Version() ? module.Version : reader.FindElfVersion(image) ?? module.Version);

        public override ModuleKind Kind => module.Kind;
        public override Version Version => _version.Value;
        public override ImmutableArray<byte> BuildId => module.BuildId;
        public override bool IsManaged => module.IsManaged;
        public override int IndexFileSize => module.IndexFileSize;
        public override int IndexTimeStamp => module.IndexTimeStamp;
        protected override long ImageSizeCore => module.ImageSize;

        public override ulong GetExportSymbolAddress(string symbol)
        {
            return module.GetExportSymbolAddress(symbol) is not 0 and var address ? address : reader.FindElfExport(image, symbol);
        }
    }
}
