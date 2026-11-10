using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OotSceneViewer;

// Read-only BFRES v10 geometry. Binary layouts and format identifiers were
// referenced from the MIT-licensed BfresLibrary by Syroot and KillzXGaming.
// See CREDITS.md and licenses/BfresLibrary.txt. No GPL library is required.
static class BfresGeometry
{
    internal record Shape(string Name, Vector3[] Positions, Vector3[]? Normals, Vector2[]? Uvs, uint[] Indices, string[] Textures);
    internal record Model(string Name, List<Shape> Shapes);

    internal static List<Model> Read(byte[] bytes)
    {
        var r = new Reader(bytes);
        r.Signature(0, "FRES");
        if ((r.U32(8) >> 16) != 10 || r.U16(12) != 0xfeff)
            throw new NotSupportedException("Only little-endian BFRES v10 geometry is supported.");
        int modelCount = r.U16(0xdc), modelTable = r.Pointer(0x28), info = r.Pointer(0xb0);
        if (modelCount > 512)
            throw new InvalidDataException("Invalid BFRES model count.");
        int gpu = (r.Byte(0xee) & 8) != 0 ? checked((int)r.U32(0x1c) + 288) : r.Pointer(info + 8);
        var result = new List<Model>();
        for (int m = 0; m < modelCount; m++)
        {
            int model = checked(modelTable + m * 0x78);
            r.Signature(model, "FMDL");
            int shapeTable = r.Pointer(model + 0x28), shapeCount = r.U16(model + 0x6a), materialTable = r.Pointer(model + 0x38), materialCount = r.U16(model + 0x6c);
            var textures = new string[materialCount][];
            for (int i = 0; i < materialCount; i++)
            {
                int material = checked(materialTable + i * 0xb0);
                r.Signature(material, "FMAT");
                int names = r.Pointer(material + 0x20), count = r.Byte(material + 0xa3);
                var refs = new string[count];
                for (int j = 0; j < count; j++)
                    refs[j] = r.String(r.Pointer(names + j * 8));
                textures[i] = refs;
            }
            var buffers = new Dictionary<int, Dictionary<string, Vector4[]>>();
            var shapes = new List<Shape>();
            for (int i = 0; i < shapeCount; i++)
            {
                int shape = checked(shapeTable + i * 0x60);
                r.Signature(shape, "FSHP");
                if (r.Byte(shape + 0x5b) == 0)
                    continue;
                int mesh = r.Pointer(shape + 0x18);
                if (r.U32(mesh + 0x24) != 3)
                    continue;
                int vertex = r.Pointer(shape + 0x10);
                if (!buffers.TryGetValue(vertex, out var attributes))
                {
                    attributes = ReadVertices(r, vertex, gpu);
                    buffers.Add(vertex, attributes);
                }
                if (!attributes.TryGetValue("_p0", out var positions))
                    continue;
                int count = checked((int)r.U32(mesh + 0x2c)), indexSize = r.U32(mesh + 0x28) switch
                {
                    0 => 1,
                    1 => 2,
                    2 => 4,
                    _ => throw new NotSupportedException("Unsupported BFRES index format.")
                };
                if (count < 0 || count > 15000000 || count % 3 != 0)
                    throw new InvalidDataException("Invalid BFRES triangle count.");
                int start = checked(gpu + (int)r.U32(mesh + 0x20));
                r.Check(start, checked(count * indexSize));
                uint first = r.U32(mesh + 0x30);
                var indices = new uint[count];
                for (int j = 0; j < count; j++)
                {
                    int at = checked(start + j * indexSize);
                    uint index = indexSize switch
                    {
                        1 => r.Byte(at),
                        2 => r.U16(at),
                        _ => r.U32(at)
                    };
                    indices[j] = checked(index + first);
                    if (indices[j] >= positions.Length)
                        throw new InvalidDataException("BFRES index exceeds its vertex buffer.");
                }
                Vector3[] V3(Vector4[] values) => values.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();
                int mat = r.U16(shape + 0x52);
                shapes.Add(new(r.String(r.Pointer(shape + 8)), V3(positions), attributes.TryGetValue("_n0", out var normals) ? V3(normals) : null, attributes.TryGetValue("_u0", out var uv) ? uv.Select(v => new Vector2(v.X, v.Y)).ToArray() : null, indices, mat < textures.Length ? textures[mat] : []));
            }
            result.Add(new(r.String(r.Pointer(model + 8)), shapes));
        }
        return result;
    }

    private static Dictionary<string, Vector4[]> ReadVertices(Reader r, int vertex, int gpu)
    {
        r.Signature(vertex, "FVTX");
        int attrTable = r.Pointer(vertex + 8), sizes = r.Pointer(vertex + 0x30), strides = r.Pointer(vertex + 0x38);
        int attrCount = r.Byte(vertex + 0x4c), bufferCount = r.Byte(vertex + 0x4d), count = checked((int)r.U32(vertex + 0x50)), alignment = r.U16(vertex + 0x56);
        if (count < 0 || count > 5000000 || bufferCount > 32 || alignment > 4096 || (alignment != 0 && (alignment & (alignment - 1)) != 0))
            throw new InvalidDataException("Invalid BFRES vertex buffer.");
        if (alignment == 0)
            alignment = 8;
        int cursor = checked(gpu + (int)r.U32(vertex + 0x48));
        var starts = new int[bufferCount];
        var lengths = new int[bufferCount];
        var steps = new int[bufferCount];
        for (int i = 0; i < bufferCount; i++)
        {
            cursor = checked((cursor + alignment - 1) & ~(alignment - 1));
            starts[i] = cursor;
            lengths[i] = checked((int)r.U32(sizes + i * 16));
            steps[i] = checked((int)r.U32(strides + i * 16));
            r.Check(cursor, lengths[i]);
            cursor = checked(cursor + lengths[i]);
        }
        var result = new Dictionary<string, Vector4[]>();
        for (int i = 0; i < attrCount; i++)
        {
            int p = checked(attrTable + i * 16);
            string name = r.String(r.Pointer(p));
            if (name is not ("_p0" or "_n0" or "_u0"))
                continue;
            ushort format = r.U16Big(p + 8);
            int offset = r.U16(p + 12), buffer = r.U16(p + 14), size = FormatSize(format);
            if (buffer >= bufferCount || steps[buffer] < 1 || offset + size > steps[buffer] || (long)(count - 1) * steps[buffer] + offset + size > lengths[buffer])
                throw new InvalidDataException("BFRES attribute exceeds its buffer.");
            var values = new Vector4[count];
            for (int j = 0; j < count; j++)
                values[j] = ReadVector(r, checked(starts[buffer] + j * steps[buffer] + offset), format);
            result.Add(name, values);
        }
        return result;
    }
    private static int FormatSize(ushort format) => (format & 255) switch { 0x02 => 1, 0x09 or 0x0a => 2, 0x0b or 0x0e or 0x12 or 0x14 or 0x16 => 4, 0x15 or 0x17 => 8, 0x18 => 12, 0x19 => 16, _ => throw new NotSupportedException($"Unsupported BFRES vertex format 0x{format:X4}.") };
    private static Vector4 ReadVector(Reader r, int offset, ushort format)
    {
        int kind = format & 255, type = format >> 8;
        if (format == 0x020e)
        {
            uint v = r.U32(offset);
            float Signed(int shift)
            {
                int n = (int)(v >> shift) & 1023;
                if (n >= 512)
                    n -= 1024;
                return Math.Max(-1, n / 511f);
            }
            return new(Signed(0), Signed(10), Signed(20), (v >> 30) / 3f);
        }
        (int bits, int components) = kind switch
        {
            0x02 => (8, 1),
            0x09 => (8, 2),
            0x0b => (8, 4),
            0x0a => (16, 1),
            0x12 => (16, 2),
            0x15 => (16, 4),
            0x14 or 0x16 => (32, 1),
            0x17 => (32, 2),
            0x18 => (32, 3),
            0x19 => (32, 4),
            _ => throw new NotSupportedException($"Unsupported BFRES vector 0x{format:X4}.")
        };
        var value = new Vector4(0, 0, 0, 1);
        for (int c = 0; c < components; c++)
        {
            int p = offset + c * bits / 8;
            float f;
            if (type == 5)
                f = bits == 16 ? (float)BitConverter.UInt16BitsToHalf(r.U16(p)) : r.Float(p);
            else
            {
                uint u = bits switch
                {
                    8 => r.Byte(p),
                    16 => r.U16(p),
                    _ => r.U32(p)
                };
                int s = bits switch
                {
                    8 => unchecked((sbyte)u),
                    16 => unchecked((short)u),
                    _ => unchecked((int)u)
                };
                f = type switch
                {
                    1 => u / (bits == 8 ? 255f : 65535f),
                    2 => Math.Max(-1, s / (bits == 8 ? 127f : 32767f)),
                    3 or 8 => u,
                    4 or 10 => s,
                    _ => throw new NotSupportedException($"Unsupported BFRES component 0x{format:X4}.")
                };
            }
            if (!float.IsFinite(f))
                throw new InvalidDataException("Non-finite BFRES vertex.");
            value[c] = f;
        }
        return value;
    }
    private sealed class Reader(byte[] data)
    {
        internal void Check(int p, int count)
        {
            if (p < 0 || count < 0 || p > data.Length - count)
                throw new InvalidDataException($"BFRES read outside resource: 0x{p:X} + {count}.");
        }
        internal byte Byte(int p)
        {
            Check(p, 1);
            return data[p];
        }
        internal ushort U16(int p)
        {
            Check(p, 2);
            return BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p, 2));
        }
        internal ushort U16Big(int p)
        {
            Check(p, 2);
            return BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(p, 2));
        }
        internal uint U32(int p)
        {
            Check(p, 4);
            return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p, 4));
        }
        internal float Float(int p) => BitConverter.UInt32BitsToSingle(U32(p));
        internal int Pointer(int p)
        {
            Check(p, 8);
            ulong value = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(p, 8));
            if (value > (ulong)data.Length)
                throw new InvalidDataException($"BFRES pointer outside resource at 0x{p:X}.");
            return (int)value;
        }
        internal string String(int p)
        {
            if (p == 0)
                return "";
            int length = U16(p);
            Check(p + 2, length);
            return Encoding.UTF8.GetString(data, p + 2, length);
        }
        internal void Signature(int p, string value)
        {
            Check(p, 4);
            if (!data.AsSpan(p, 4).SequenceEqual(Encoding.ASCII.GetBytes(value)))
                throw new InvalidDataException($"Expected {value} at BFRES offset 0x{p:X}.");
        }
    }
}
