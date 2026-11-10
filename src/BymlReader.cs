using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace OotSceneViewer;

// BYML layout checked against the MIT-licensed oot-remake-modding zstool.
sealed class BymlReader
{
    private readonly byte[] data;
    private readonly bool little;
    private readonly string[] keys;
    private readonly string[] strings;
    private readonly HashSet<int> active = [];
    internal JsonNode Root
    {
        get;
    }

    public BymlReader(byte[] bytes)
    {
        data = bytes;
        if (data.Length < 16)
            throw new InvalidDataException("BYML header is truncated.");
        little = data[0] == (byte)'Y' && data[1] == (byte)'B';
        if (!little && !(data[0] == (byte)'B' && data[1] == (byte)'Y'))
            throw new InvalidDataException("Invalid BYML signature.");
        try
        {
            int keyOffset = Offset(U32(4)), stringOffset = Offset(U32(8)), root = Offset(U32(12));
            keys = keyOffset == 0 ? [] : Table(keyOffset);
            strings = stringOffset == 0 ? [] : Table(stringOffset);
            Root = root == 0 ? throw new InvalidDataException("BYML root is missing.") : Node(root, 0);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException) { throw new InvalidDataException("BYML contains invalid offsets.", ex); }
    }
    private ReadOnlySpan<byte> Span(int offset, int length) => data.AsSpan(offset, length);
    private uint U32(int p) => little ? BinaryPrimitives.ReadUInt32LittleEndian(Span(p, 4)) : BinaryPrimitives.ReadUInt32BigEndian(Span(p, 4));
    private ulong U64(int p) => little ? BinaryPrimitives.ReadUInt64LittleEndian(Span(p, 8)) : BinaryPrimitives.ReadUInt64BigEndian(Span(p, 8));
    private int U24(int p)
    {
        var b = Span(p, 3);
        return little ? b[0] | b[1] << 8 | b[2] << 16 : b[2] | b[1] << 8 | b[0] << 16;
    }
    private static int Offset(uint offset) => checked((int)offset);
    private int Count(int p)
    {
        int n = U24(p + 1);
        if (n > 1_000_000)
            throw new InvalidDataException("BYML node has too many values.");
        return n;
    }
    private string[] Table(int p)
    {
        if (data[p] != 0xc2)
            throw new InvalidDataException("Invalid BYML string table.");
        int n = Count(p);
        var result = new string[n];
        for (int i = 0; i < n; i++)
        {
            int at = checked(p + Offset(U32(checked(p + 4 + i * 4))));
            int end = Array.IndexOf(data, (byte)0, at);
            if (end < 0)
                throw new InvalidDataException("Unterminated BYML string.");
            result[i] = Encoding.UTF8.GetString(data, at, end - at);
        }
        return result;
    }
    private JsonNode? Value(byte type, uint raw, int depth)
    {
        switch (type)
        {
            case 0xc0:
            case 0xc1:
            case 0x20:
            case 0x21:
                return Node(Offset(raw), depth + 1);
            case 0xa0:
                if (raw >= strings.Length)
                    throw new InvalidDataException("Invalid BYML string index.");
                return JsonValue.Create(strings[raw]);
            case 0xa1:
                {
                    int p = Offset(raw), n = Offset(U32(p));
                    return JsonValue.Create(Convert.ToBase64String(Span(checked(p + 4), n)));
                }
            case 0xd0:
                return JsonValue.Create(raw != 0);
            case 0xd1:
                return JsonValue.Create(unchecked((int)raw));
            case 0xd2:
                {
                    float value = BitConverter.Int32BitsToSingle(unchecked((int)raw));
                    if (!float.IsFinite(value))
                        throw new InvalidDataException("Non-finite BYML number.");
                    return JsonValue.Create(value);
                }
            case 0xd3:
                return JsonValue.Create(raw);
            case 0xd4:
                return JsonValue.Create(unchecked((long)U64(Offset(raw))));
            case 0xd5:
                return JsonValue.Create(U64(Offset(raw)));
            case 0xd6:
                {
                    double value = BitConverter.Int64BitsToDouble(unchecked((long)U64(Offset(raw))));
                    if (!double.IsFinite(value))
                        throw new InvalidDataException("Non-finite BYML number.");
                    return JsonValue.Create(value);
                }
            case 0xff:
                return null;
            default:
                throw new InvalidDataException($"Unsupported BYML value type 0x{type:X2}.");
        }
    }
    private JsonNode Node(int p, int depth)
    {
        if (depth > 128 || !active.Add(p))
            throw new InvalidDataException("BYML contains a cycle or excessive nesting.");
        try
        {
            byte type = data[p];
            int n = Count(p);
            if (type == 0xc0)
            {
                int values = checked(p + 4 + ((n + 3) & ~3));
                var array = new JsonArray();
                for (int i = 0; i < n; i++)
                    array.Add(Value(data[checked(p + 4 + i)], U32(checked(values + 4 * i)), depth));
                return array;
            }
            if (type == 0xc1)
            {
                var obj = new JsonObject();
                for (int i = 0; i < n; i++)
                {
                    int at = checked(p + 4 + 8 * i), key = U24(at);
                    if (key >= keys.Length)
                        throw new InvalidDataException("Invalid BYML key index.");
                    obj.Add(keys[key], Value(data[at + 3], U32(at + 4), depth));
                }
                return obj;
            }
            if (type is 0x20 or 0x21)
            {
                int size = type == 0x20 ? 8 : 12, types = checked(p + 4 + size * n);
                var obj = new JsonObject();
                for (int i = 0; i < n; i++)
                {
                    int at = checked(p + 4 + size * i);
                    ulong key = type == 0x20 ? U32(at) : U64(at);
                    obj.Add("0x" + key.ToString("x", CultureInfo.InvariantCulture), Value(data[types + i], U32(at + size - 4), depth));
                }
                return obj;
            }
            throw new InvalidDataException($"Unsupported BYML node type 0x{type:X2}.");
        }
        finally { active.Remove(p); }
    }
}
