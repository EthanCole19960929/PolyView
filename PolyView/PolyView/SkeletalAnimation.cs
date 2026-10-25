using System.Numerics;

namespace PolyView;

internal sealed record AnimationNode(string Name, int Parent, Matrix4x4 BindLocal);
internal sealed record AnimationTrack(int Node, double[] PositionTimes, Vector3[] Positions, double[] RotationTimes, Quaternion[] Rotations, double[] ScaleTimes, Vector3[] Scales);
internal sealed record SkinBone(int Node, Matrix4x4 Offset, (int Vertex, float Weight)[] Weights);

/// <summary>Owned CPU animation data. No pointers into the native Assimp scene survive loading.</summary>
public sealed class SkeletalAnimation
{
    public MeshData Mesh { get; }
    public double Duration { get; }
    readonly AnimationNode[] _nodes;
    readonly AnimationTrack?[] _tracks;
    readonly SkinBone[] _bones;
    readonly Vector3[] _positions, _normals;
    readonly int[] _owners;
    readonly float[] _weightSums;
    readonly Matrix4x4[] _worlds, _normalWorlds;
    readonly Vector3[] _bindPositions, _bindScales;
    readonly Quaternion[] _bindRotations;
    readonly float _scale;
    readonly Vector3 _offset;
    readonly Dictionary<string, int> _nodeLookup;

    internal SkeletalAnimation(MeshData mesh, AnimationNode[] nodes, AnimationTrack[] tracks, SkinBone[] bones,
        Vector3[] positions, Vector3[] normals, int[] owners, double duration, float scale, Vector3 offset)
    {
        Mesh = mesh; _nodes = nodes; _bones = bones; _positions = positions; _normals = normals; _owners = owners;
        Duration = duration; _scale = scale; _offset = offset;
        _tracks = new AnimationTrack?[nodes.Length];
        _nodeLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Length; i++) _nodeLookup.TryAdd(nodes[i].Name, i);
        foreach (var track in tracks) _tracks[track.Node] = track;
        _worlds = new Matrix4x4[nodes.Length]; _normalWorlds = new Matrix4x4[nodes.Length];
        _bindPositions = new Vector3[nodes.Length]; _bindScales = new Vector3[nodes.Length]; _bindRotations = new Quaternion[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
            if (!Matrix4x4.Decompose(nodes[i].BindLocal, out _bindScales[i], out _bindRotations[i], out _bindPositions[i]))
                throw new InvalidOperationException($"无法分解动画节点变换：{nodes[i].Name}");
        _weightSums = new float[positions.Length];
        foreach (var bone in bones) foreach (var weight in bone.Weights) _weightSums[weight.Vertex] += weight.Weight;
    }

    internal bool TryGetNodeWorld(string name, out Matrix4x4 world)
    {
        if (_nodeLookup.TryGetValue(name, out int index))
        {
            world = _worlds[index];
            return true;
        }

        world = Matrix4x4.Identity;
        return false;
    }

    internal Vector3 ToMeshLocal(Vector3 sourcePosition) => sourcePosition * _scale + _offset;

    public void Evaluate(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, Duration);
        for (int i = 0; i < _nodes.Length; i++)
        {
            var local = _nodes[i].BindLocal;
            if (_tracks[i] is { } track)
            {
                var p = Sample(track.PositionTimes, track.Positions, seconds, _bindPositions[i]);
                var r = Sample(track.RotationTimes, track.Rotations, seconds, _bindRotations[i]);
                var s = Sample(track.ScaleTimes, track.Scales, seconds, _bindScales[i]);
                local = Matrix4x4.CreateScale(s) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(p);
            }
            _worlds[i] = _nodes[i].Parent < 0 ? local : local * _worlds[_nodes[i].Parent];
            Matrix4x4.Invert(_worlds[i], out var inverse);
            _normalWorlds[i] = Matrix4x4.Transpose(inverse);
        }
        var vertices = Mesh.Vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].Position = _weightSums[i] > 0 ? Vector3.Zero : Vector3.Transform(_positions[i], _worlds[_owners[i]]);
            vertices[i].Normal = _weightSums[i] > 0 ? Vector3.Zero : Vector3.TransformNormal(_normals[i], _normalWorlds[_owners[i]]);
        }
        foreach (var bone in _bones)
        {
            var matrix = bone.Offset * _worlds[bone.Node];
            Matrix4x4.Invert(matrix, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);
            foreach (var (vertex, weight) in bone.Weights)
            {
                vertices[vertex].Position += Vector3.Transform(_positions[vertex], matrix) * weight;
                vertices[vertex].Normal += Vector3.TransformNormal(_normals[vertex], normalMatrix) * weight;
            }
        }
        var min = new Vector3(float.MaxValue); var max = new Vector3(float.MinValue);
        for (int i = 0; i < vertices.Length; i++)
        {
            if (_weightSums[i] > 0) vertices[i].Position /= _weightSums[i];
            vertices[i].Position = vertices[i].Position * _scale + _offset;
            var n = vertices[i].Normal;
            vertices[i].Normal = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
            min = Vector3.Min(min, vertices[i].Position); max = Vector3.Max(max, vertices[i].Position);
        }
        Mesh.UpdateBounds(min, max);
    }

    static int Interval(double[] times, double time)
    {
        int index = Array.BinarySearch(times, time);
        return index >= 0 ? index : Math.Max(0, ~index - 1);
    }

    static Vector3 Sample(double[] times, Vector3[] values, double time, Vector3 fallback)
    {
        if (times.Length == 0) return fallback;
        if (time <= times[0]) return values[0];
        int i = Interval(times, time);
        if (i >= times.Length - 1) return values[^1];
        double span = times[i + 1] - times[i];
        return Vector3.Lerp(values[i], values[i + 1], span > 0 ? (float)((time - times[i]) / span) : 0);
    }

    static Quaternion Sample(double[] times, Quaternion[] values, double time, Quaternion fallback)
    {
        if (times.Length == 0) return fallback;
        if (time <= times[0]) return Quaternion.Normalize(values[0]);
        int i = Interval(times, time);
        if (i >= times.Length - 1) return Quaternion.Normalize(values[^1]);
        double span = times[i + 1] - times[i];
        return Quaternion.Normalize(Quaternion.Slerp(values[i], values[i + 1], span > 0 ? (float)((time - times[i]) / span) : 0));
    }
}
