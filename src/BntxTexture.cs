using System.Buffers.Binary;
using Vortice.DXGI;
using AstcSharp;
using AstcSharp.Core;

namespace OotSceneViewer;

// Reads the first 2D mip of supported Ounce BNTX formats.
// Nonstandard channel mappings remain unsupported.
sealed record BntxTexture(int Width, int Height, Format Format, int BlockBytes, byte[] Data)
{
    internal int PaddedWidth => BlockBytes == 4 ? Width : (Width + 3) & ~3;
    internal int PaddedHeight => BlockBytes == 4 ? Height : (Height + 3) & ~3;
    internal int RowPitch => BlockBytes == 4 ? Width * 4 : PaddedWidth / 4 * BlockBytes;

    internal static BntxTexture Decode(byte[] b, int layer = 0, int maxSize = 2048)
    {
        uint U32(int p)
        {
            if (p < 0 || p > b.Length - 4)
                throw new InvalidDataException("Invalid BNTX offset.");
            return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p, 4));
        }
        int Ptr(int p)
        {
            if (p < 0 || p > b.Length - 8)
                throw new InvalidDataException("Invalid BNTX pointer.");
            ulong n = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(p, 8));
            return n <= (ulong)b.Length ? (int)n : throw new InvalidDataException("BNTX pointer outside file.");
        }
        if (b.Length < 0x58 || !b.AsSpan(0, 4).SequenceEqual("BNTX"u8) || !b.AsSpan(0x20, 4).SequenceEqual("Ounc"u8) || U32(0x24) != 1)
            throw new InvalidDataException("Unsupported BNTX container.");
        int brti = Ptr(Ptr(0x28));
        if (brti > b.Length - 0x98 || !b.AsSpan(brti, 4).SequenceEqual("BRTI"u8))
            throw new InvalidDataException("Invalid BNTX texture header.");
        int width = checked((int)U32(brti + 0x24)), height = checked((int)U32(brti + 0x28));
        uint type = U32(brti + 0x1c), blockHeightLog = U32(brti + 0x34) & 7;
        int layers = checked((int)U32(brti + 0x30));
        int mips = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(brti + 0x16, 2));
        if (width < 1 || height < 1 || width > 16384 || height > 16384 || U32(brti + 0x2c) != 1 || layers < 1 || layers > 256 || layer < 0 || layer >= layers || blockHeightLog > 5 || (b[brti + 0x5c] != 1 && b[brti + 0x5c] != 5) || BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(brti + 0x12, 2)) != 0)
            throw new InvalidDataException("Unsupported BNTX dimensions or tile mode.");
        if (!b.AsSpan(brti + 0x58, 4).SequenceEqual(new byte[] { 2, 3, 4, 5 }))
            throw new InvalidDataException("Unsupported BNTX channel mapping.");
        bool srgb = (type & 0xff) == 6;
        uint kind = type >> 8;
        bool astc = kind >= 0x2d && kind <= 0x3a;
        (Format format, int bytes) = kind switch
        {
            0x1a => (srgb ? Format.BC1_UNorm_SRgb : Format.BC1_UNorm, 8),
            0x1b => (srgb ? Format.BC2_UNorm_SRgb : Format.BC2_UNorm, 16),
            0x1c => (srgb ? Format.BC3_UNorm_SRgb : Format.BC3_UNorm, 16),
            0x20 => (srgb ? Format.BC7_UNorm_SRgb : Format.BC7_UNorm, 16),
            >= 0x2d and <= 0x3a => (srgb ? Format.R8G8B8A8_UNorm_SRgb : Format.R8G8B8A8_UNorm, 16),
            _ => throw new NotSupportedException($"Unsupported BNTX format 0x{type:X}.")
        };
        if ((type & 0xff) != 1 && !srgb)
            throw new NotSupportedException("Unsupported BNTX color format.");
        (int X, int Y, FootprintType Type)[] footprints = [(4, 4, FootprintType.Footprint4x4), (5, 4, FootprintType.Footprint5x4), (5, 5, FootprintType.Footprint5x5), (6, 5, FootprintType.Footprint6x5), (6, 6, FootprintType.Footprint6x6), (8, 5, FootprintType.Footprint8x5), (8, 6, FootprintType.Footprint8x6), (8, 8, FootprintType.Footprint8x8), (10, 5, FootprintType.Footprint10x5), (10, 6, FootprintType.Footprint10x6), (10, 8, FootprintType.Footprint10x8), (10, 10, FootprintType.Footprint10x10), (12, 10, FootprintType.Footprint12x10), (12, 12, FootprintType.Footprint12x12)];
        var footprint = astc ? footprints[kind - 0x2d] : (4, 4, FootprintType.Footprint4x4);
        int mip = 0;
        while ((width > maxSize || height > maxSize) && mip + 1 < mips)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            mip++;
        }
        if (width > maxSize || height > maxSize || maxSize < 1 || maxSize > 2048)
            throw new NotSupportedException("Texture has no supported mip size.");
        int pointerTable = Ptr(brti + 0x70), start = Ptr(pointerTable + 8 * mip);
        int imageSize = checked((int)U32(brti + 0x50));
        if (imageSize % layers != 0)
            throw new InvalidDataException("Invalid BNTX array stride.");
        start = checked(start + layer * (imageSize / layers));
        int blocksX = (width + footprint.Item1 - 1) / footprint.Item1, blocksY = (height + footprint.Item2 - 1) / footprint.Item2;
        int blockHeight = 1 << (int)blockHeightLog, pitchGobs = (blocksX * bytes + 63) / 64;
        while (blockHeight > 1 && blocksY <= blockHeight * 4)
            blockHeight /= 2;
        var output = new byte[checked(blocksX * blocksY * bytes)];
        for (int y = 0; y < blocksY; y++)
            for (int x = 0; x < blocksX; x++)
            {
                int xb = x * bytes;
                int gob = (y / (8 * blockHeight)) * 512 * blockHeight * pitchGobs + (xb / 64) * 512 * blockHeight + (y % (8 * blockHeight) / 8) * 512;
                int intra = ((xb % 64) / 32) * 256 + ((y % 8) / 2) * 64 + ((xb % 32) / 16) * 32 + (y % 2) * 16 + (xb % 16);
                // Ounce swaps bits 5 and 6 of the GOB-local sector address.
                intra = (intra & ~96) | ((intra & 32) << 1) | ((intra & 64) >> 1);
                int src = checked(start + gob + intra);
                if (src < 0 || src > b.Length - bytes)
                    throw new InvalidDataException("Truncated BNTX image data.");
                b.AsSpan(src, bytes).CopyTo(output.AsSpan((y * blocksX + x) * bytes, bytes));
            }
        if (astc)
        {
            using var input = new MemoryStream(output, false);
            using var rgba = new MemoryStream(checked(width * height * 4));
            AstcDecoder.DecompressImage(input, rgba, width, height, Footprint.FromFootprintType(footprint.Item3), srgb ? LdrDecodeMode.Srgb : LdrDecodeMode.Linear);
            if (rgba.Length != (long)width * height * 4)
                throw new InvalidDataException("ASTC output size mismatch.");
            return new BntxTexture(width, height, format, 4, rgba.ToArray());
        }
        return new BntxTexture(width, height, format, bytes, output);
    }

    internal void Save(string path)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var w = new BinaryWriter(file);
        w.Write(0x31584554);
        w.Write(Width);
        w.Write(Height);
        w.Write((int)Format);
        w.Write(BlockBytes);
        w.Write(Data.Length);
        w.Write(Data);
    }
    internal static BntxTexture Load(string path)
    {
        using var file = File.OpenRead(path);
        using var r = new BinaryReader(file);
        if (r.ReadInt32() != 0x31584554)
            throw new InvalidDataException("Invalid cached texture.");
        int w = r.ReadInt32(), h = r.ReadInt32();
        var format = (Format)r.ReadInt32();
        int block = r.ReadInt32(), length = r.ReadInt32();
        if (w < 1 || h < 1 || w > 2048 || h > 2048 || (block != 4 && block != 8 && block != 16))
            throw new InvalidDataException("Invalid cached texture dimensions.");
        bool allowed = block switch
        {
            4 => format is Format.R8G8B8A8_UNorm or Format.R8G8B8A8_UNorm_SRgb,
            8 => format is Format.BC1_UNorm or Format.BC1_UNorm_SRgb,
            16 => format is Format.BC2_UNorm or Format.BC2_UNorm_SRgb or Format.BC3_UNorm or Format.BC3_UNorm_SRgb or Format.BC7_UNorm or Format.BC7_UNorm_SRgb,
            _ => false
        };
        int expected = block == 4 ? checked(w * h * 4) : checked(((w + 3) / 4) * ((h + 3) / 4) * block);
        if (!allowed || length != expected || length != file.Length - file.Position)
            throw new InvalidDataException("Invalid cached texture format or size.");
        return new BntxTexture(w, h, format, block, r.ReadBytes(length));
    }
}
