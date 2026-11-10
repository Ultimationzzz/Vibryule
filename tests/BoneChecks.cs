using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace OotSceneViewer;

static class BoneChecks
{
    internal static string Run()
    {
        // A small synthetic BFRES with two shapes sharing one vertex buffer.
        // One uses a translated root; the other a scaled, rotated child bone.
        byte[] data = new byte[0x1000];
        void U16(int at, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at), value);
        void U32(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(at), value);
        void Ptr(int at, int value) => BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(at), value);
        void F(int at, float value) => U32(at, BitConverter.SingleToUInt32Bits(value));
        void Sig(int at, string value) => Encoding.ASCII.GetBytes(value).CopyTo(data, at);
        void Str(int at, string value)
        {
            U16(at, (ushort)value.Length);
            Sig(at + 2, value);
        }
        void V3(int at, Vector3 value)
        {
            F(at, value.X);
            F(at + 4, value.Y);
            F(at + 8, value.Z);
        }
        Sig(0, "FRES");
        U32(8, 10 << 16);
        U16(12, 0xfeff);
        U16(0xdc, 1);
        Ptr(0x28, 0x100);
        Ptr(0xb0, 0x540);
        Ptr(0x548, 0x800);
        Sig(0x100, "FMDL");
        Ptr(0x108, 0x6c0);
        Str(0x6c0, "fixture");
        Ptr(0x118, 0x180);
        Ptr(0x128, 0x300);
        U16(0x16a, 2);
        Sig(0x180, "FSKL");
        Ptr(0x190, 0x200);
        U16(0x1b8, 2);
        void Bone(int at, ushort parent, Vector3 scale, Vector3 rotation, Vector3 translation)
        {
            U16(at + 0x22, parent);
            U32(at + 0x2c, 0x1000);
            V3(at + 0x30, scale);
            V3(at + 0x3c, rotation);
            F(at + 0x48, 1);
            V3(at + 0x4c, translation);
        }
        Bone(0x200, 0xffff, Vector3.One, Vector3.Zero, new(10, 0, 0));
        Bone(0x258, 0, new(2, 1, 3), new(0, MathF.PI / 2, 0), new(2, 0, 0));
        for (int i = 0; i < 2; i++)
        {
            int at = 0x300 + i * 0x60;
            Sig(at, "FSHP");
            Ptr(at + 8, 0x680 + i * 0x20);
            Str(0x680 + i * 0x20, "shape" + i);
            Ptr(at + 0x10, 0x400);
            Ptr(at + 0x18, 0x500);
            U16(at + 0x54, (ushort)i);
            data[at + 0x5b] = 1;
        }
        Sig(0x400, "FVTX");
        Ptr(0x408, 0x480);
        Ptr(0x430, 0x4a0);
        Ptr(0x438, 0x4b0);
        data[0x44c] = 2;
        data[0x44d] = 1;
        U32(0x450, 3);
        U16(0x456, 8);
        U32(0x4a0, 72);
        U32(0x4b0, 24);
        for (int i = 0; i < 2; i++)
        {
            int at = 0x480 + i * 16;
            Ptr(at, 0x600 + i * 0x20);
            Str(0x600 + i * 0x20, i == 0 ? "_p0" : "_n0");
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at + 8), 0x0518);
            U16(at + 12, (ushort)(i * 12));
        }
        U32(0x520, 0x100);
        U32(0x524, 3);
        U32(0x528, 1);
        U32(0x52c, 3);
        for (int i = 0; i < 3; i++)
        {
            V3(0x800 + i * 24, new(1, i, 0));
            V3(0x80c + i * 24, Vector3.UnitZ);
            U16(0x900 + i * 2, (ushort)i);
        }
        var shapes = BfresGeometry.Read(data).Single().Shapes;
        Near(shapes[0].Positions[0], new(11, 0, 0), "Root transform");
        Near(shapes[1].Positions[0], new(12, 0, -2), "Child rest transform");
        Near(Vector3.Normalize(shapes[1].Normals![0]), Vector3.UnitX, "Inverse-transpose normal");
        Near(shapes[0].Positions[0], new(11, 0, 0), "Shared vertex buffer was mutated");
        var q = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
        var matrix = BonePose.WorldMatrices([new(-1, Vector3.One, new(q.X, q.Y, q.Z, q.W), Vector3.Zero, false)])[0];
        Near(Vector3.Transform(Vector3.UnitX, matrix), -Vector3.UnitZ, "Quaternion bone");
        foreach (var bones in new BonePose[][] {
            [new(0, Vector3.One, Vector4.UnitW, Vector3.Zero, false)],
            [new(2, Vector3.One, Vector4.UnitW, Vector3.Zero, false)],
            [new(-1, Vector3.One, new(float.NaN,0,0,1), Vector3.Zero, false)] })
        {
            try
            {
                BonePose.WorldMatrices(bones);
                throw new InvalidOperationException("Invalid bone hierarchy accepted.");
            }
            catch (InvalidDataException) { }
        }
        return "PASS: BFRES rigid shapes apply rest hierarchy once, with scale, rotation, translation and correct normals; invalid bones rejected.";
    }

    private static void Near(Vector3 actual, Vector3 expected, string message)
    {
        if (Vector3.Distance(actual, expected) > 0.0001f)
            throw new InvalidOperationException($"{message}: {actual}, expected {expected}.");
    }
}
