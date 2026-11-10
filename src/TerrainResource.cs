using System.Buffers.Binary;
using ZstdSharp;
using System.Numerics;
using System.Text;

namespace OotSceneViewer;

// Ounce TSCB 13.1 and TARB v1. CPU acceleration data is separate from the
// block-linear GPU texture. All offsets below are relative to their own fields.
static class TerrainResource
{
    internal record Tile(string Name, float X, float Z, float Size, int Quadrants = 15);
    internal record Material(int Layer, Vector2 Scale, Vector3 Color);
    internal record Descriptor(float HeightScale, List<Tile> Tiles, List<Material> Materials);

    internal static Descriptor ReadDescriptor(byte[] data)
    {
        var r = new Reader(data);
        if (data.Length < 0x7c || !data.AsSpan(0, 4).SequenceEqual("TSCB"u8) || r.Int(4) != 0x10d)
            throw new InvalidDataException("Unsupported terrain descriptor (expected TSCB 13.1).");
        float world = r.Float(0x10), height = r.Float(0x14);
        if (world <= 0 || height <= 0)
            throw new InvalidDataException("Invalid terrain scale.");
        int count = r.Int(0x38), table = r.Relative(0x78), materialCount = r.Int(0x34), materialsTable = r.Relative(0x74);
        if (count < 1 || count > 100000 || materialCount < 1 || materialCount > 127)
            throw new InvalidDataException("Invalid terrain table size.");
        var tiles = new List<Tile>();
        var materials = new List<Material>();
        for (int i = 0; i < count; i++)
        {
            int p = r.Relative(checked(table + i * 4));
            var name = r.String(r.Relative(p + 0x60));
            if (name.Length != 10 || name.Any(c => !char.IsAsciiHexDigit(c)))
                throw new InvalidDataException("Invalid terrain tile name.");
            float size = r.Float(p + 8) * world;
            if (size <= 0)
                throw new InvalidDataException("Invalid terrain tile size.");
            tiles.Add(new(name, r.Float(p) * world, r.Float(p + 4) * world, size));
        }
        for (int i = 0; i < materialCount; i++)
        {
            int p = r.Relative(checked(materialsTable + i * 4));
            materials.Add(new(r.Int(p), new(r.Float(p + 0x10), r.Float(p + 0x14)), new(r.Float(p + 0x30), r.Float(p + 0x34), r.Float(p + 0x38))));
        }
        return new(height, tiles, materials);
    }

    internal static List<Tile> SelectTiles(Descriptor descriptor, Func<string, bool> exists, float targetSize = 125)
    {
        var available = descriptor.Tiles.Where(t => exists(t.Name)).ToList();
        var selected = new List<Tile>();
        void Visit(Tile tile)
        {
            var children = available.Where(t => Math.Abs(t.Size - tile.Size / 2) < 0.001f && Math.Abs(t.X - tile.X) < tile.Size / 2 && Math.Abs(t.Z - tile.Z) < tile.Size / 2).ToArray();
            if (tile.Size > targetSize && children.Length > 0)
            {
                int covered = 0;
                foreach (var child in children)
                    covered |= 1 << ((child.X > tile.X ? 1 : 0) + (child.Z > tile.Z ? 2 : 0));
                if (covered != 15)
                    selected.Add(tile with
                    {
                        Quadrants = 15 & ~covered
                    });
                foreach (var child in children)
                    Visit(child);
            }
            else
                selected.Add(tile);
        }
        foreach (var root in available.Where(t => !available.Any(p => p.Size > t.Size && Math.Abs(p.X - t.X) + t.Size / 2 <= p.Size / 2 + 0.001f && Math.Abs(p.Z - t.Z) + t.Size / 2 <= p.Size / 2 + 0.001f)))
            Visit(root);
        return selected;
    }

    internal static float[] ReadHeights(byte[] archive)
    {
        byte[] data = ExpandHeightResource(archive);
        var r = new Reader(data);
        if (r.Int(0) != 0x01020401 || r.Int(4) != 11 || r.Int(12) != 257 || (r.Int(24) & 0xffff) != 257)
            throw new InvalidDataException("Unsupported terrain height texture.");
        int start = r.Relative(0x2c);
        if (r.Int(0x30) != 0x66000 || start != data.Length - 0x66000)
            throw new InvalidDataException("Invalid terrain height texture size.");
        var heights = new float[257 * 257];
        for (int y = 0; y < 257; y++)
            for (int x = 0; x < 257; x++)
            {
                float h = r.Float(start + TextureOffset(x, y));
                // R32 float heights are scaled by the descriptor, not UNORM.
                // Castle terrain includes negative elevations; preserve them.
                // Reader.Float still rejects NaN and infinity.
                heights[y * 257 + x] = h;
            }
        return heights;
    }

    internal static (byte[] Indices, byte[] Weights) ReadMaterials(byte[] archive)
    {
        byte[] data = ExpandHeightResource(archive);
        var r = new Reader(data);
        if (r.Int(0) != 0x02020401 || r.Int(4) != 0x06060606 || (r.Int(24) & 0xffff) != 257 || r.Int(0x30) != 0xcc000)
            throw new InvalidDataException("Unsupported terrain material texture.");
        int start = r.Relative(0x2c);
        if (start != data.Length - 0xcc000)
            throw new InvalidDataException("Invalid terrain material texture size.");
        var indices = new byte[257 * 257 * 4];
        var weights = new byte[indices.Length];
        for (int y = 0; y < 257; y++)
            for (int x = 0; x < 257; x++)
            {
                int from = start + TextureOffset(x, y), to = (y * 257 + x) * 4;
                data.AsSpan(from, 4).CopyTo(indices.AsSpan(to));
                data.AsSpan(from + 0x66000, 4).CopyTo(weights.AsSpan(to));
                // Material references use seven bits; bit 7 is a per-layer flag.
                for (int c = 0; c < 4; c++)
                    indices[to + c] &= 0x7f;
            }
        return (indices, weights);
    }

    private static int TextureOffset(int x, int y)
    {
        const int blockHeight = 16, pitchGobs = 17;
        int xb = x * 4;
        int gob = y / (8 * blockHeight) * 512 * blockHeight * pitchGobs + xb / 64 * 512 * blockHeight + y % (8 * blockHeight) / 8 * 512;
        int q = xb % 64 / 32 * 256 + y % 8 / 2 * 64 + xb % 32 / 16 * 32 + y % 2 * 16 + xb % 16;
        return gob + (q & ~96) + ((q & 32) << 1) + ((q & 64) >> 1);
    }

    private sealed class Reader(byte[] data)
    {
        internal int Int(int p)
        {
            if (p < 0 || p > data.Length - 4)
                throw new InvalidDataException("Terrain offset outside resource.");
            return BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p, 4));
        }
        internal float Float(int p)
        {
            float f = BitConverter.Int32BitsToSingle(Int(p));
            return float.IsFinite(f) ? f : throw new InvalidDataException("Non-finite terrain value.");
        }
        internal int Relative(int p)
        {
            int target = checked(p + Int(p));
            if (target < 0 || target >= data.Length)
                throw new InvalidDataException("Invalid terrain relative pointer.");
            return target;
        }
        internal string String(int p)
        {
            int length = data.AsSpan(p).IndexOf((byte)0);
            if (length < 0 || length > 128)
                throw new InvalidDataException("Invalid terrain string.");
            return Encoding.UTF8.GetString(data, p, length);
        }
    }
    private const int MaxExpandedLength = 64 * 1024 * 1024;

    internal static byte[] ExpandHeightResource(byte[] data)
    {
        if (data.Length < 20 || !data.AsSpan(0, 4).SequenceEqual("TARB"u8))
            throw new InvalidDataException("Not a TARB terrain resource.");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        int headerSize = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
        uint expandedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12));
        uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16));
        if (version != 1 || headerSize < 20 || headerSize > data.Length ||
            compressedSize != data.Length - headerSize || expandedSize > MaxExpandedLength)
            throw new InvalidDataException("Unsupported or invalid TARB header.");
        using var source = new MemoryStream(data, headerSize, (int)compressedSize, false);
        using var stream = new DecompressionStream(source);
        using var output = new MemoryStream((int)expandedSize);
        byte[] buffer = new byte[65536];
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (output.Length + count > expandedSize)
                throw new InvalidDataException("TARB payload exceeds its declared size.");
            output.Write(buffer, 0, count);
        }
        if (output.Length != expandedSize)
            throw new InvalidDataException("TARB payload size does not match its header.");
        return output.ToArray();
    }
}
