using System.Runtime.InteropServices;

namespace Ncma;

[StructLayout(LayoutKind.Sequential)]
public struct Vector3(float x, float y, float z)
{
    public float X = x;
    public float Y = y;
    public float Z = z;
}

[StructLayout(LayoutKind.Sequential)]
public struct Transform
{
    public Vector3 Position;
    public float RotationX;
    public float RotationY;
    public float RotationZ;
    public float RotationW;
    public Vector3 Scale;

    public static Transform Identity => new()
    {
        RotationW = 1.0f,
        Scale = new Vector3(1.0f, 1.0f, 1.0f)
    };
}

