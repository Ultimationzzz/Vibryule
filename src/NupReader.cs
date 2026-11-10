using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;
using ZstdSharp;

namespace OotSceneViewer;

// Reads the NUP v1 layout documented by Lichii's NupContainer. Game data remains local.
sealed class NupReader : IDisposable
{
    private const int MaxToc = 256 * 1024 * 1024;
    private const int MaxMember = 512 * 1024 * 1024;
    private const uint NoDir = uint.MaxValue;
    private readonly FileStream stream;
    private readonly DateTime initialWriteTime;
    private readonly long initialLength;
    private readonly Package[] packages;
    private readonly Dir[] directories;
    private readonly Member[] members;
    private readonly uint[] children;
    private readonly int[] activePackages;
    private readonly HashSet<string> visibleDirectories = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, Entry> Files = new(StringComparer.Ordinal);
    internal string SourcePath
    {
        get;
    }
    internal string Version
    {
        get;
    }

    private record Package(ulong Title, uint Romfs, uint Version, byte Kind);
    private record Dir(string Name, uint First, uint Count);
    private record Member(string Name, ulong Size, ulong Offset, ulong StoredSize, byte Codec, byte[] Hash);
    internal record Entry(string Path, int PackageIndex, int MemberIndex);
    private static readonly Regex Placement = new(@"^/Banc/([^/]+)/([^/]+)_(Static|Dynamic)\.bcett\.byml\.zs$", RegexOptions.CultureInvariant);

    public NupReader(string path)
    {
        SourcePath = Path.GetFullPath(path);
        var info = new FileInfo(SourcePath);
        initialLength = info.Length;
        initialWriteTime = info.LastWriteTimeUtc;
        stream = new FileStream(SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.RandomAccess);
        try
        {
            var super = ReadAt(0, 128);
            var h = super.AsSpan();
            if (!h[..4].SequenceEqual(new byte[] { (byte)'N', (byte)'U', (byte)'P', 1 }) || U16(h, 4) < 128 || U32(h, 76) != 0)
                throw new InvalidDataException("Unsupported or invalid NUP header.");
            ulong tocOffset = U64(h, 8), tocSize = U64(h, 16), blob = U64(h, 56), fileSize = U64(h, 64);
            uint packageCount = U32(h, 72);
            if (fileSize != (ulong)initialLength || tocOffset < 128 || tocOffset > blob || blob > fileSize || tocSize > blob - tocOffset || (blob & 15) != 0 || tocSize > MaxToc)
                throw new InvalidDataException("Invalid NUP table bounds.");
            var toc = ReadAt(tocOffset, checked((int)tocSize));
            var t = toc.AsSpan();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(toc), h.Slice(24, 32)) || t.Length < 48 || U32(t, 0) != 0x01434f54 || U32(t, 4) != packageCount || packageCount == 0)
                throw new InvalidDataException("NUP table is corrupt or has an invalid checksum.");
            ulong dirCount = U64(t, 8), fileCount = U64(t, 16), pool = U64(t, 24), poolSize = U64(t, 32);
            if (dirCount > (ulong)t.Length / 24 || fileCount > (ulong)t.Length / 80 || packageCount > (uint)t.Length / 64)
                throw new InvalidDataException("Invalid NUP table counts.");
            ulong needed = checked(48UL + (ulong)packageCount * 64 + dirCount * 24 + fileCount * 80);
            if (needed > (ulong)t.Length || pool < needed || pool > (ulong)t.Length || poolSize > (ulong)t.Length - pool || (pool - needed) % 4 != 0)
                throw new InvalidDataException("Invalid NUP table layout.");
            int childCount = checked((int)((pool - needed) / 4));
            int pos = 48;
            packages = new Package[packageCount];
            directories = new Dir[checked((int)dirCount)];
            members = new Member[checked((int)fileCount)];
            children = new uint[childCount];
            for (int i = 0; i < packages.Length; i++, pos += 64)
            {
                ulong title = U64(t, pos);
                uint root = U32(t, pos + 8), exefs = U32(t, pos + 12), romfs = U32(t, pos + 16), version = U32(t, pos + 20);
                byte kind = t[pos + 24], flags = t[pos + 25];
                if (kind > 2 || (flags & ~1) != 0 || root >= dirCount || (exefs != NoDir && exefs >= dirCount) || (romfs != NoDir && romfs >= dirCount))
                    throw new InvalidDataException("Invalid NUP package record.");
                packages[i] = new Package(title, romfs, version, kind);
            }
            if (!packages.Any(p => p.Title == 0x0400486024DEC000UL))
                throw new InvalidDataException("This NUP is not the supported Ocarina of Time remake.");
            string Name(uint offset, uint length)
            {
                if ((ulong)offset + length > poolSize)
                    throw new InvalidDataException("Invalid NUP string bounds.");
                return new UTF8Encoding(false, true).GetString(toc, checked((int)(pool + offset)), checked((int)length));
            }
            for (int i = 0; i < directories.Length; i++, pos += 24)
            {
                uint no = U32(t, pos), nl = U32(t, pos + 4), first = U32(t, pos + 8), count = U32(t, pos + 12);
                if ((ulong)first + count > (ulong)childCount)
                    throw new InvalidDataException("Invalid NUP directory children.");
                directories[i] = new Dir(Name(no, nl), first, count);
            }
            for (int i = 0; i < members.Length; i++, pos += 80)
            {
                uint no = U32(t, pos), nl = U32(t, pos + 4);
                ulong size = U64(t, pos + 8), off = U64(t, pos + 16), stored = U64(t, pos + 24);
                byte codec = t[pos + 64];
                if (codec > 1 || t[pos + 65] != 0 || (size == 0 && (off != 0 || stored != 0)) || (size != 0 && (stored == 0 || off < blob || (off & 15) != 0 || off > fileSize || stored > fileSize - off)))
                    throw new InvalidDataException("Invalid NUP member bounds.");
                members[i] = new Member(Name(no, nl), size, off, stored, codec, t.Slice(pos + 32, 32).ToArray());
            }
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = U32(t, pos + i * 4);
                if (children[i] >= dirCount + fileCount)
                    throw new InvalidDataException("Invalid NUP child index.");
            }
            var basePackage = packages.Select((p, i) => (p, i)).FirstOrDefault(x => x.p.Kind == 0);
            var updatePackage = packages.Select((p, i) => (p, i)).Where(x => x.p.Kind == 1).OrderByDescending(x => x.p.Version).FirstOrDefault();
            var active = new[] { basePackage, updatePackage }.Where(x => x.p != null && x.p.Romfs != NoDir).ToArray();
            if (active.Length == 0)
                throw new InvalidDataException("NUP contains no usable RomFS package.");
            activePackages = active.Select(x => x.i).ToArray();
            foreach (var (package, index) in active)
                Walk(index, checked((int)package.Romfs), "", new HashSet<int>(), new HashSet<string>(StringComparer.Ordinal), 0);
            Version = active[^1].p.Version.ToString();
        }
        catch { stream.Dispose(); throw; }
    }

    private void Walk(int package, int directory, string parent, HashSet<int> stack, HashSet<string> seen, int depth)
    {
        if (depth > 128 || !stack.Add(directory))
            throw new InvalidDataException("NUP directory cycle or excessive depth.");
        var dir = directories[directory];
        for (uint i = 0; i < dir.Count; i++)
        {
            uint child = children[checked((int)(dir.First + i))];
            if (child < (uint)directories.Length)
            {
                var name = directories[child].Name;
                CheckName(name);
                string path = parent + "/" + name;
                if (!seen.Add(path))
                    throw new InvalidDataException("Duplicate NUP path.");
                Files.Remove(path);
                visibleDirectories.Add(path);
                Walk(package, checked((int)child), path, stack, seen, depth + 1);
            }
            else
            {
                int index = checked((int)(child - (uint)directories.Length));
                var name = members[index].Name;
                CheckName(name);
                string path = parent + "/" + name;
                if (!seen.Add(path))
                    throw new InvalidDataException("Duplicate NUP path.");
                if (visibleDirectories.Remove(path))
                {
                    string prefix = path + "/";
                    foreach (var existing in Files.Keys.Where(p => p.StartsWith(prefix, StringComparison.Ordinal)).ToArray())
                        Files.Remove(existing);
                    visibleDirectories.RemoveWhere(p => p.StartsWith(prefix, StringComparison.Ordinal));
                }
                Files[path] = new Entry(path, package, index);
            }
        }
        stack.Remove(directory);
    }

    private static void CheckName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or ".." || name.Any(c => c < 32 || c is '/' or '\\'))
            throw new InvalidDataException("Invalid NUP entry name.");
    }
    internal List<SceneOption> Scenes() => Files.Keys.Select(path => Placement.Match(path)).Where(m => m.Success)
        .GroupBy(m => (Folder: m.Groups[1].Value, Name: m.Groups[2].Value))
        .Select(g => new SceneOption(g.Key.Folder, g.Key.Name, g.Select(m => m.Groups[3].Value).Distinct().Order().ToArray()))
        .OrderBy(s => s.Folder, StringComparer.Ordinal).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
    internal IEnumerable<Entry> PackageDictionaries()
    {
        foreach (int i in activePackages)
        {
            var found = FindInPackage(i, "/Pack/ZsDic.pack.zs");
            if (found != null)
                yield return found;
        }
    }
    private Entry? FindInPackage(int package, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        int dir = checked((int)packages[package].Romfs);
        for (int part = 0; part < parts.Length; part++)
        {
            var d = directories[dir];
            bool found = false;
            for (uint i = 0; i < d.Count; i++)
            {
                uint child = children[checked((int)(d.First + i))];
                if (child < (uint)directories.Length && part < parts.Length - 1 && directories[child].Name == parts[part])
                {
                    dir = (int)child;
                    found = true;
                    break;
                }
                if (child >= (uint)directories.Length && part == parts.Length - 1)
                {
                    int idx = (int)(child - (uint)directories.Length);
                    if (members[idx].Name == parts[part])
                        return new Entry(path, package, idx);
                }
            }
            if (!found)
                return null;
        }
        return null;
    }
    internal byte[] ReadMember(Entry entry, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var member = members[entry.MemberIndex];
        if (member.Size > MaxMember || member.StoredSize > MaxMember)
            throw new InvalidDataException($"NUP member exceeds the {MaxMember / 1024 / 1024} MiB safety limit: {entry.Path}");
        byte[] raw = ReadAt(member.Offset, checked((int)member.StoredSize));
        byte[] output;
        if (member.Codec == 0)
        {
            if (member.Size != member.StoredSize)
                throw new InvalidDataException("Invalid uncompressed member size.");
            output = raw;
        }
        else
        {
            output = new byte[checked((int)member.Size)];
            using var decoder = new Decompressor();
            if (decoder.Unwrap(raw, output) != output.Length)
                throw new InvalidDataException("NUP member decompressed to the wrong size.");
        }
        cancellation.ThrowIfCancellationRequested();
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(output), member.Hash))
            throw new InvalidDataException($"NUP member checksum mismatch: {entry.Path}");
        return output;
    }
    internal void VerifyUnchanged()
    {
        var info = new FileInfo(SourcePath);
        if (!info.Exists || info.Length != initialLength || info.LastWriteTimeUtc != initialWriteTime)
            throw new IOException("The NUP changed while it was being read.");
    }
    internal string Identity() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{SourcePath}\0{initialLength}\0{initialWriteTime.Ticks}")))[..20].ToLowerInvariant();
    private byte[] ReadAt(ulong offset, int size)
    {
        if (offset > (ulong)initialLength || (ulong)size > (ulong)initialLength - offset)
            throw new InvalidDataException("Read outside NUP file.");
        var result = new byte[size];
        int total = 0;
        SafeFileHandle handle = stream.SafeFileHandle;
        while (total < size)
        {
            int n = RandomAccess.Read(handle, result.AsSpan(total), checked((long)offset + total));
            if (n == 0)
                throw new EndOfStreamException("Unexpected end of NUP.");
            total += n;
        }
        return result;
    }
    private static ushort U16(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(p, 2));
    private static uint U32(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(p, 4));
    private static ulong U64(ReadOnlySpan<byte> b, int p) => BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(p, 8));
    public void Dispose() => stream.Dispose();
}
