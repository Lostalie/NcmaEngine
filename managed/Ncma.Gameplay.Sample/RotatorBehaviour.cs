using Ncma;

namespace Ncma.Gameplay.Sample;

public sealed class RotatorBehaviour : Behaviour
{
    [Export(DisplayName = "Speed", Category = "Rotation")]
    public float DegreesPerSecond { get; set; } = 45.0f;

    [Export(Category = "Rotation")]
    public bool Clockwise { get; set; } = true;

    [Export(Category = "Rotation")]
    public int Multiplier = 1;

    public double ElapsedSeconds { get; private set; }

    protected override void OnUpdate(double deltaSeconds)
    {
        ElapsedSeconds += deltaSeconds;
        Transform transform = GameObject.LocalTransform;
        var rotation = new System.Numerics.Quaternion(
            transform.RotationX, transform.RotationY, transform.RotationZ, transform.RotationW);
        float angle = DegreesPerSecond * Multiplier * (Clockwise ? 1 : -1) * (float)deltaSeconds * MathF.PI / 180;
        rotation = System.Numerics.Quaternion.Normalize(rotation *
            System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitY, angle));
        transform.RotationX = rotation.X;
        transform.RotationY = rotation.Y;
        transform.RotationZ = rotation.Z;
        transform.RotationW = rotation.W;
        GameObject.LocalTransform = transform;
    }
}
