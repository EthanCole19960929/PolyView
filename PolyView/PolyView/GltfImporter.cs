using System.IO;
using System.Numerics;
using SharpGLTF.Schema2;

namespace PolyView;

/// <summary>
/// 读取 glTF 2.0（.glb / .gltf）。把所有网格按节点层级变换合并成一个 MeshData；
/// 蒙皮模型按默认姿态（绑定姿态）显示，不播放动画。
/// </summary>
public static class GltfImporter
{
    public static MeshData Load(string path)
    {
        var settings = new ReadSettings { Validation = SharpGLTF.Validation.ValidationMode.TryFix };
        var model = ModelRoot.Load(path, settings);

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        var parts = new List<MeshPart>();
        var textures = new List<MeshTexture>();
        var textureSlots = new Dictionary<int, int>();

        IEnumerable<Node> roots = model.DefaultScene?.VisualChildren
            ?? model.LogicalNodes.Where(n => n.VisualParent == null);
        foreach (var node in Flatten(roots))
        {
            if (node.Mesh == null)
            {
                continue;
            }

            Matrix4x4[]? jointMatrices = null;
            if (node.Skin != null && node.Skin.JointsCount > 0)
            {
                jointMatrices = new Matrix4x4[node.Skin.JointsCount];
                for (int i = 0; i < jointMatrices.Length; i++)
                {
                    var (joint, inverseBind) = node.Skin.GetJoint(i);
                    jointMatrices[i] = inverseBind * joint.WorldMatrix;
                }
            }

            var world = node.WorldMatrix;
            foreach (var primitive in node.Mesh.Primitives)
            {
                AddPrimitive(primitive, world, jointMatrices, vertices, indices, parts, textures, textureSlots);
            }
        }

        if (indices.Count < 3)
        {
            throw new InvalidDataException("glTF 里没有可显示的三角形。");
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex.Position);
            max = Vector3.Max(max, vertex.Position);
        }

        return new MeshData
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Vertices = vertices.ToArray(),
            Indices = indices.ToArray(),
            Min = min,
            Max = max,
            Parts = parts.ToArray(),
            Textures = textures.ToArray(),
        };
    }

    static IEnumerable<Node> Flatten(IEnumerable<Node> nodes)
    {
        var stack = new Stack<Node>(nodes.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            foreach (var child in node.VisualChildren.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    static void AddPrimitive(
        MeshPrimitive primitive,
        Matrix4x4 world,
        Matrix4x4[]? jointMatrices,
        List<Vertex> vertices,
        List<uint> indices,
        List<MeshPart> parts,
        List<MeshTexture> textures,
        Dictionary<int, int> textureSlots)
    {
        var positionAccessor = primitive.GetVertexAccessor("POSITION");
        if (positionAccessor == null)
        {
            return;
        }

        var triangles = primitive.GetTriangleIndices().ToList();
        if (triangles.Count == 0)
        {
            return;
        }

        var positions = positionAccessor.AsVector3Array();
        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = ReadColors(primitive.GetVertexAccessor("COLOR_0"));
        var joints = jointMatrices == null ? null : primitive.GetVertexAccessor("JOINTS_0")?.AsVector4Array();
        var weights = jointMatrices == null ? null : primitive.GetVertexAccessor("WEIGHTS_0")?.AsVector4Array();
        bool skinned = joints != null && weights != null;

        var (baseColor, textureSlot, uvSet) = ReadMaterial(primitive.Material, textures, textureSlots);
        if (uvSet != 0)
        {
            uvs = primitive.GetVertexAccessor($"TEXCOORD_{uvSet}")?.AsVector2Array() ?? uvs;
        }

        uint baseVertex = (uint)vertices.Count;
        int count = positions.Count;
        var local = new Vertex[count];
        for (int i = 0; i < count; i++)
        {
            var matrix = skinned ? Blend(jointMatrices!, joints![i], weights![i]) : world;
            var position = Vector3.Transform(positions[i], matrix);
            var normal = Vector3.Zero;
            if (normals != null)
            {
                normal = TransformNormal(normals[i], matrix);
            }

            local[i] = new Vertex(position, normal)
            {
                TexCoord = uvs != null && i < uvs.Count ? uvs[i] : Vector2.Zero,
                Color = baseColor * (colors != null && i < colors.Length ? colors[i] : Vector4.One),
            };
        }

        // 负缩放会翻转三角形朝向，非蒙皮网格需要换回来。
        bool flip = !skinned && world.GetDeterminant() < 0;
        int start = indices.Count;
        var faceNormals = normals == null ? new Vector3[count] : null;
        foreach (var (a, b, c) in triangles)
        {
            if (a >= count || b >= count || c >= count)
            {
                continue;
            }

            int ib = flip ? c : b;
            int ic = flip ? b : c;
            indices.Add(baseVertex + (uint)a);
            indices.Add(baseVertex + (uint)ib);
            indices.Add(baseVertex + (uint)ic);
            if (faceNormals != null)
            {
                var n = Vector3.Cross(local[ib].Position - local[a].Position, local[ic].Position - local[a].Position);
                faceNormals[a] += n;
                faceNormals[ib] += n;
                faceNormals[ic] += n;
            }
        }

        for (int i = 0; i < count; i++)
        {
            var n = faceNormals != null ? faceNormals[i] : local[i].Normal;
            local[i].Normal = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
        }

        vertices.AddRange(local);
        int indexCount = indices.Count - start;
        if (indexCount > 0)
        {
            parts.Add(new MeshPart
            {
                StartIndex = start,
                IndexCount = indexCount,
                TextureIndex = textureSlot,
                DoubleSided = primitive.Material?.DoubleSided ?? false,
            });
        }
    }

    static Matrix4x4 Blend(Matrix4x4[] jointMatrices, Vector4 joints, Vector4 weights)
    {
        float total = weights.X + weights.Y + weights.Z + weights.W;
        if (total <= 1e-6f)
        {
            return Matrix4x4.Identity;
        }

        var result = new Matrix4x4();
        Accumulate(ref result, jointMatrices, joints.X, weights.X / total);
        Accumulate(ref result, jointMatrices, joints.Y, weights.Y / total);
        Accumulate(ref result, jointMatrices, joints.Z, weights.Z / total);
        Accumulate(ref result, jointMatrices, joints.W, weights.W / total);
        return result;
    }

    static void Accumulate(ref Matrix4x4 result, Matrix4x4[] jointMatrices, float joint, float weight)
    {
        int index = (int)joint;
        if (weight <= 0f || index < 0 || index >= jointMatrices.Length)
        {
            return;
        }

        result += jointMatrices[index] * weight;
    }

    static Vector3 TransformNormal(Vector3 normal, Matrix4x4 matrix)
    {
        if (Matrix4x4.Invert(matrix, out var inverse))
        {
            return Vector3.TransformNormal(normal, Matrix4x4.Transpose(inverse));
        }

        return Vector3.TransformNormal(normal, matrix);
    }

    static Vector4[]? ReadColors(Accessor? accessor)
    {
        if (accessor == null)
        {
            return null;
        }

        if (accessor.Dimensions == DimensionType.VEC3)
        {
            return accessor.AsVector3Array().Select(c => new Vector4(c, 1f)).ToArray();
        }

        if (accessor.Dimensions == DimensionType.VEC4)
        {
            return accessor.AsVector4Array().ToArray();
        }

        return null;
    }

    static (Vector4 Color, int TextureSlot, int UvSet) ReadMaterial(
        Material? material,
        List<MeshTexture> textures,
        Dictionary<int, int> textureSlots)
    {
        if (material == null)
        {
            return (Vector4.One, -1, 0);
        }

        var channel = material.FindChannel("BaseColor") ?? material.FindChannel("Diffuse");
        if (channel == null)
        {
            return (Vector4.One, -1, 0);
        }

        var color = channel.Value.Color;
        var texture = channel.Value.Texture;
        if (texture?.PrimaryImage == null)
        {
            return (color, -1, 0);
        }

        int key = texture.PrimaryImage.LogicalIndex;
        if (!textureSlots.TryGetValue(key, out int slot))
        {
            slot = -1;
            var decoded = TextureDecoder.Decode(texture.PrimaryImage.Content.Content.ToArray());
            if (decoded != null)
            {
                slot = textures.Count;
                textures.Add(decoded);
            }

            textureSlots[key] = slot;
        }

        return (color, slot, channel.Value.TextureCoordinate);
    }
}
