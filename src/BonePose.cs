using System.Numerics;

namespace OotSceneViewer;

// Static rest transforms only. Weighted skinning and animation remain separate.
readonly record struct BonePose(int Parent, Vector3 Scale, Vector4 Rotation, Vector3 Translation, bool Euler)
{
    internal static Matrix4x4[] WorldMatrices(IReadOnlyList<BonePose> bones)
    {
        var result = new Matrix4x4[bones.Count];
        var state = new byte[bones.Count];
        Matrix4x4 Visit(int index, int depth)
        {
            if (state[index] == 2)
                return result[index];
            if (state[index] == 1 || depth > 256)
                throw new InvalidDataException("Cyclic or excessively deep BFRES bone hierarchy.");
            state[index] = 1;
            var b = bones[index];
            if (!float.IsFinite(b.Scale.LengthSquared()) || !float.IsFinite(b.Rotation.LengthSquared()) || !float.IsFinite(b.Translation.LengthSquared()))
                throw new InvalidDataException("Non-finite BFRES bone transform.");
            if (b.Parent < -1 || b.Parent >= bones.Count)
                throw new InvalidDataException("BFRES bone parent outside skeleton.");
            Matrix4x4 rotation;
            if (b.Euler)
                rotation = Matrix4x4.CreateRotationX(b.Rotation.X) * Matrix4x4.CreateRotationY(b.Rotation.Y) * Matrix4x4.CreateRotationZ(b.Rotation.Z);
            else
            {
                var q = new Quaternion(b.Rotation.X, b.Rotation.Y, b.Rotation.Z, b.Rotation.W);
                if (q.LengthSquared() < 1e-12f)
                    throw new InvalidDataException("Invalid BFRES bone quaternion.");
                rotation = Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q));
            }
            var matrix = Matrix4x4.CreateScale(b.Scale) * rotation * Matrix4x4.CreateTranslation(b.Translation);
            if (b.Parent >= 0)
                matrix *= Visit(b.Parent, depth + 1);
            if (!float.IsFinite(matrix.GetDeterminant()))
                throw new InvalidDataException("BFRES bone transform overflow.");
            result[index] = matrix;
            state[index] = 2;
            return matrix;
        }
        for (int i = 0; i < bones.Count; i++)
            Visit(i, 0);
        return result;
    }
}
