using System.Numerics;

namespace PolyView;

/// <summary>
/// 围绕目标点旋转的相机（Y 轴朝上）。Yaw = 0、Pitch = 0 时从 +Z 方向看向目标，相当于 Blender 的前视图。
/// </summary>
public struct OrbitCamera
{
    /// <summary>俯仰角上限，略小于 90°，避免和世界 Y 轴共线导致 LookAt 失效。</summary>
    public const float MaxPitch = MathF.PI / 2f - 0.001f;

    public Vector3 Target;
    public float Yaw;
    public float Pitch;
    public float Distance;
    public float FieldOfView;
    public bool Orthographic;

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

    /// <summary>正交投影时可见区域的半高，和透视下目标平面的半高一致，切换时画面大小不跳。</summary>
    public readonly float OrthoHalfHeight => Distance * MathF.Tan(FieldOfView * 0.5f);

    /// <summary>把包围盒放到画面中央：目标 = 包围盒中心，距离按包围球半径和视角算，保证整个模型在画面内。</summary>
    public void Frame(Vector3 min, Vector3 max)
    {
        var extent = (max - min) * 0.5f;
        float radius = MathF.Max(extent.Length(), 0.001f);
        Target = (min + max) * 0.5f;
        Yaw = 0.6f;
        Pitch = 0.35f;
        Distance = radius / MathF.Sin(FieldOfView * 0.5f) * 1.1f;
    }

    /// <summary>只改视角方向，保持目标和距离（Blender 小键盘 1/3/7）。</summary>
    public void SetView(float yaw, float pitch)
    {
        Yaw = yaw;
        Pitch = Math.Clamp(pitch, -MaxPitch, MaxPitch);
    }

    public void Orbit(float deltaX, float deltaY)
    {
        Yaw -= deltaX;
        Pitch = Math.Clamp(Pitch + deltaY, -MaxPitch, MaxPitch);
    }

    /// <summary>滚轮缩放：朝目标点推进 / 拉远。</summary>
    public void Zoom(int wheelDelta)
    {
        float steps = wheelDelta / 120f;
        Distance = Math.Clamp(Distance * MathF.Pow(0.88f, steps), 0.0001f, 1e6f);
    }

    /// <summary>Ctrl + 中键拖动的连续缩放，向上拖拉近。</summary>
    public void ZoomDrag(float deltaY)
    {
        Distance = Math.Clamp(Distance * MathF.Exp(deltaY * 0.01f), 0.0001f, 1e6f);
    }

    /// <summary>平移：鼠标移动多少像素，目标点在屏幕上就跟着移动多少。</summary>
    public void Pan(float deltaXPixels, float deltaYPixels, float viewportHeightPixels)
    {
        float worldPerPixel = 2f * OrthoHalfHeight / MathF.Max(1f, viewportHeightPixels);
        var forward = Vector3.Normalize(Target - Eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Normalize(Vector3.Cross(right, forward));
        Target -= right * (deltaXPixels * worldPerPixel);
        Target += up * (deltaYPixels * worldPerPixel);
    }
}
