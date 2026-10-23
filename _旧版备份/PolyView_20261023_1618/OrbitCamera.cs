using System.Numerics;

namespace PolyView;

public struct OrbitCamera
{
    public Vector3 Target;
    public float Yaw;
    public float Pitch;
    public float Distance;
    public float FieldOfView;

    public static OrbitCamera Default()
    {
        return new OrbitCamera
        {
            Target = Vector3.Zero,
            Yaw = 0f,
            Pitch = 0f,
            Distance = 3f,
            FieldOfView = 45f * MathF.PI / 180f,
        };
    }

    public readonly Vector3 Eye
    {
        get
        {
            float cosPitch = MathF.Cos(Pitch);
            var offset = new Vector3(MathF.Sin(Yaw) * cosPitch, MathF.Sin(Pitch), MathF.Cos(Yaw) * cosPitch);
            return Target + offset * Distance;
        }
    }

    public void Frame(Vector3 min, Vector3 max)
    {
        var extent = (max - min) * 0.5f;
        float radius = MathF.Max(extent.Length(), 0.001f);
        Target = (min + max) * 0.5f;
        Yaw = 0f;
        Pitch = 0.25f;
        Distance = radius / MathF.Sin(FieldOfView * 0.5f) * 1.35f;
    }

    public void Orbit(float deltaX, float deltaY)
    {
        Yaw += deltaX;
        Pitch = Math.Clamp(Pitch + deltaY, -1.2f, 1.2f);
    }

    public void Zoom(int wheelDelta)
    {
        float factor = wheelDelta > 0 ? 0.9f : 1.1f;
        Distance = Math.Clamp(Distance * factor, 0.05f, 10000f);
    }

    public void Pan(float deltaX, float deltaY)
    {
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        Target -= right * deltaX;
        Target += up * deltaY;
    }
}
