using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using SharpGLTF.Schema2;

namespace PolyView;

/// <summary>
/// 读取 glTF 2.0（.glb / .gltf）。把所有网格按节点层级变换合并成一个 MeshData；
/// 蒙皮模型按默认姿态（绑定姿态）显示，不播放动画。
/// </summary>
public static class GltfImporter
{
    /// <summary>普通文件直接交给 SharpGLTF；Draco 压缩的 GLB 先解压成普通 GLB 再读。</summary>
    static ModelRoot Open(string path, ReadSettings settings)
    {
        if (Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = File.ReadAllBytes(path);
            if (DracoGlb.UsesDraco(bytes))
            {
                var decoded = DracoGlb.Decompress(bytes);
                var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                using var stream = new MemoryStream(decoded, writable: false);
                return ReadContext.CreateFromDirectory(directory).WithSettingsFrom(settings).ReadBinarySchema2(stream);
            }
        }
        else if (File.ReadAllText(path).Contains("KHR_draco_mesh_compression", StringComparison.Ordinal))
        {
            throw new DracoNotSupportedException("该 glTF 使用 Draco 压缩，目前只支持 Draco 压缩的 .glb，请用 .glb 版本。");
        }

        return ModelRoot.Load(path, settings);
    }

    public static MeshData Load(string path)
    {
        var settings = new ReadSettings { Validation = SharpGLTF.Validation.ValidationMode.TryFix };
        var model = Open(path, settings);

        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        var parts = new List<MeshPart>();
        var textures = new List<MeshTexture>();
        var textureSlots = new Dictionary<int, int>();

        IEnumerable<Node> roots = model.DefaultScene?.VisualChildren
            ?? model.LogicalNodes.Where(n => n.VisualParent == null);
        var sceneNodes = new List<SceneNode>();
        var nodeSlots = new Dictionary<Node, int>();
        foreach (var node in Flatten(roots))
        {
            int slot = sceneNodes.Count;
            int parent = node.VisualParent != null && nodeSlots.TryGetValue(node.VisualParent, out var p) ? p : -1;
            nodeSlots[node] = slot;
            int firstPart = parts.Count;
            if (node.Mesh != null)
            {
                AddNodeMesh(node, vertices, indices, parts, textures, textureSlots);
            }

            var camera = ReadCamera(node.Camera);
            var light = ReadLight(node);
            sceneNodes.Add(new SceneNode
            {
                Index = slot,
                Name = string.IsNullOrEmpty(node.Name) ? $"Node {node.LogicalIndex}" : node.Name,
                Parent = parent,
                Kind = node.Mesh != null ? SceneNodeKind.Mesh : camera != null ? SceneNodeKind.Camera : light != null ? SceneNodeKind.Light : SceneNodeKind.Empty,
                Local = node.LocalMatrix,
                World = node.WorldMatrix,
                PartIndices = Enumerable.Range(firstPart, parts.Count - firstPart).ToArray(),
                Camera = camera,
                Light = light,
            });
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
            Nodes = SceneNode.Link(sceneNodes),
            World = ReadWorld(model.DefaultScene),
        };
    }

    static void AddNodeMesh(
        Node node,
        List<Vertex> vertices,
        List<uint> indices,
        List<MeshPart> parts,
        List<MeshTexture> textures,
        Dictionary<int, int> textureSlots)
    {
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

    static SceneCamera? ReadCamera(SharpGLTF.Schema2.Camera? camera)
    {
        if (camera == null)
        {
            return null;
        }

        return camera.Settings switch
        {
            CameraPerspective p => new SceneCamera
            {
                YFov = p.VerticalFOV,
                AspectRatio = p.AspectRatio ?? 0,
                Near = p.ZNear,
                Far = float.IsFinite(p.ZFar) ? p.ZFar : 0,
            },
            CameraOrthographic o => new SceneCamera
            {
                Orthographic = true,
                OrthoHalfHeight = o.YMag,
                AspectRatio = o.YMag != 0 ? o.XMag / o.YMag : 0,
                Near = o.ZNear,
                Far = o.ZFar,
            },
            _ => new SceneCamera(),
        };
    }

    /// <summary>
    /// KHR_lights_punctual 只有点光 / 平行光 / 聚光。面光按 docs/07 §6 写在节点 extras 里：
    /// "polyview_light": {"type":"area","shape":"rectangle","size":[2,1],"power":100,"color":[r,g,b]}；
    /// 点光 / 聚光也可在 polyview_light 里给 "radius"。
    /// </summary>
    static SceneLight? ReadLight(Node node)
    {
        var extra = (node.Extras as JsonObject)?["polyview_light"] as JsonObject;
        var light = node.PunctualLight;
        if (light == null)
        {
            if (!string.Equals(Str(extra, "type"), "area", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var shape = (Str(extra, "shape") ?? "square").ToLowerInvariant() switch
            {
                "rectangle" => AreaShape.Rectangle,
                "disk" => AreaShape.Disk,
                "ellipse" => AreaShape.Ellipse,
                _ => AreaShape.Square,
            };
            var size = Vec2(extra, "size", Vector2.One);
            if (shape is AreaShape.Square or AreaShape.Disk)
            {
                size = new Vector2(size.X, size.X);
            }

            float power = Num(extra, "power", SceneLight.DefaultPointPower);
            return new SceneLight
            {
                Type = SceneLightType.Area,
                Color = Vec3(extra, "color", Vector3.One),
                Intensity = power,
                Power = power,
                Shape = shape,
                Size = size,
            };
        }

        float range = float.IsFinite(light.Range) ? light.Range : 0;
        float radius = Num(extra, "radius", SceneLight.DefaultRadius);
        switch (light.LightType)
        {
            case PunctualLightType.Directional:
                return new SceneLight
                {
                    Type = SceneLightType.Directional,
                    Color = light.Color,
                    Intensity = light.Intensity,
                    Power = SceneLight.LuxToWattsPerSquareMeter(light.Intensity),
                };
            case PunctualLightType.Spot:
                float outer = light.OuterConeAngle;
                return new SceneLight
                {
                    Type = SceneLightType.Spot,
                    Color = light.Color,
                    Intensity = light.Intensity,
                    Power = SceneLight.CandelaToWatts(light.Intensity),
                    Range = range,
                    Radius = radius,
                    InnerConeAngle = light.InnerConeAngle,
                    OuterConeAngle = outer,
                    SpotBlend = outer > 0 ? Math.Clamp(1f - light.InnerConeAngle / outer, 0f, 1f) : 0f,
                };
            default:
                return new SceneLight
                {
                    Type = light.LightType == PunctualLightType.Point ? SceneLightType.Point : SceneLightType.Unknown,
                    Color = light.Color,
                    Intensity = light.Intensity,
                    Power = SceneLight.CandelaToWatts(light.Intensity),
                    Range = range,
                    Radius = radius,
                };
        }
    }

    /// <summary>场景 extras："polyview_world": {"color":[r,g,b],"strength":1}。</summary>
    static SceneWorld ReadWorld(Scene? scene)
    {
        if ((scene?.Extras as JsonObject)?["polyview_world"] is not JsonObject world)
        {
            return new SceneWorld();
        }

        return new SceneWorld
        {
            Color = Vec3(world, "color", new Vector3(0.05f)),
            Strength = Num(world, "strength", 1f),
            FromFile = true,
        };
    }

    static Vector2 Vec2(JsonObject? o, string key, Vector2 fallback)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v))
        {
            return fallback;
        }

        if (v is JsonValue single && single.TryGetValue<double>(out var d))
        {
            return new Vector2((float)d);
        }

        if (v is JsonArray a && a.Count >= 2)
        {
            float At(int i) => a[i] is JsonValue jv && jv.TryGetValue<double>(out var x) ? (float)x : 0f;
            return new Vector2(At(0), At(1));
        }

        return fallback;
    }

    static string? Str(JsonObject? o, string key)
        => o != null && o.TryGetPropertyValue(key, out var v) && v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null;

    static float Num(JsonObject? o, string key, float fallback)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v is not JsonValue jv)
        {
            return fallback;
        }

        return jv.TryGetValue<double>(out var d) ? (float)d : fallback;
    }

    static Vector3 Vec3(JsonObject? o, string key, Vector3 fallback)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v is not JsonArray a || a.Count < 3)
        {
            return fallback;
        }

        float At(int i) => a[i] is JsonValue jv && jv.TryGetValue<double>(out var d) ? (float)d : 0f;
        return new Vector3(At(0), At(1), At(2));
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
                Color = Vertex.PackColor(baseColor * (colors != null && i < colors.Length ? colors[i] : Vector4.One)),
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

/// <summary>Draco 相关的失败（缺解码库、.gltf 外置 Draco 等），不再退回 Assimp，直接把原因显示出来。</summary>
public sealed class DracoNotSupportedException(string message) : IOException(message);
