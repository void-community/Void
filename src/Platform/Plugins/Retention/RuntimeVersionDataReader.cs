using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Diagnostics.Runtime;

namespace Void.Proxy.Plugins.Retention;

/// <summary>
/// Reads a dump as the reader it wraps does, and gives the runtime module of a Linux dump the version that reader misses. ClrMD looks for the
/// version of libcoreclr.so in its first writable segment only, while the runtime keeps it in another one, and without a version ClrMD takes
/// the runtime for one of no version at all, matches no DAC to it, and reads its heap wrongly.
/// </summary>
internal sealed class RuntimeVersionDataReader(IDataReader reader) : IDataReader
{
    private const uint LoadableSegment = 1;
    private const uint WritableSegment = 2;
    private const int ElfHeaderSize = 64;
    private const int ProgramHeaderSize = 56;

    private static readonly byte[] VersionMarker = "@(#)Version "u8.ToArray();

    public string DisplayName => reader.DisplayName;
    public bool IsThreadSafe => reader.IsThreadSafe;
    public OSPlatform TargetPlatform => reader.TargetPlatform;
    public Architecture Architecture => reader.Architecture;
    public int ProcessId => reader.ProcessId;
    public int PointerSize => reader.PointerSize;

    public IEnumerable<ModuleInfo> EnumerateModules()
    {
        return reader.EnumerateModules().Select(module => module.Kind is ModuleKind.Elf && module.Version == new Version() && FindElfVersion(module.ImageBase) is { } version ? new VersionedModuleInfo(module, version) : module);
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

    // Searches every writable segment that the ELF image of a 64-bit module loads, as ClrMD does with the first one.
    private Version? FindElfVersion(ulong imageBase)
    {
        Span<byte> header = stackalloc byte[ElfHeaderSize];

        if (reader.Read(imageBase, header) < ElfHeaderSize || !header[..4].SequenceEqual("\u007fELF"u8) || header[4] is not 2)
            return null;

        var programHeadersOffset = BinaryPrimitives.ReadUInt64LittleEndian(header[0x20..]);
        var programHeaderEntrySize = BinaryPrimitives.ReadUInt16LittleEndian(header[0x36..]);
        var programHeaderCount = BinaryPrimitives.ReadUInt16LittleEndian(header[0x38..]);
        Span<byte> programHeader = stackalloc byte[ProgramHeaderSize];

        for (var index = 0; index < programHeaderCount; index++)
        {
            if (reader.Read(imageBase + programHeadersOffset + (ulong)(index * programHeaderEntrySize), programHeader) < ProgramHeaderSize)
                return null;

            var type = BinaryPrimitives.ReadUInt32LittleEndian(programHeader);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(programHeader[4..]);

            if (type is not LoadableSegment || (flags & WritableSegment) is 0)
                continue;

            var virtualAddress = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x10..]);
            var memorySize = BinaryPrimitives.ReadUInt64LittleEndian(programHeader[0x28..]);

            if (FindVersion(imageBase + virtualAddress, memorySize) is { } version)
                return version;
        }

        return null;
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

    private sealed class VersionedModuleInfo(ModuleInfo module, Version version) : ModuleInfo(module.ImageBase, module.FileName)
    {
        public override ModuleKind Kind => module.Kind;
        public override Version Version => version;
        public override ImmutableArray<byte> BuildId => module.BuildId;
        public override bool IsManaged => module.IsManaged;
        public override int IndexFileSize => module.IndexFileSize;
        public override int IndexTimeStamp => module.IndexTimeStamp;
        protected override long ImageSizeCore => module.ImageSize;

        public override ulong GetExportSymbolAddress(string symbol)
        {
            return module.GetExportSymbolAddress(symbol);
        }
    }
}
