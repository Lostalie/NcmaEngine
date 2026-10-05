using System.Numerics;

namespace Ncma.Assets;

public static class AssetMatrices
{
    // System.Numerics uses row vectors: row-major flattening encodes its transpose as a column-major matrix.
    public static float[] EncodeColumnMajor(Matrix4x4 value)
    {
        float[] result = [value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44];
        if (result.Any(v => !float.IsFinite(v))) throw new ArgumentException("Asset matrix must be finite.");
        return result;
    }
}
