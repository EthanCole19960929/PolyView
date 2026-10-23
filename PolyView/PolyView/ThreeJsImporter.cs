using System.IO;
using System.Numerics;
using System.Text.Json;

namespace PolyView;

/// <summary>
/// 读 three.js 的 JSON 场景 / 物体格式（Object3D.toJSON()、ObjectLoader、three.js 编辑器「导出场景」，metadata.version 4.x）。
/// 支持 BufferGeometry（position / normal / uv / color + index + groups）以及 Box / Sphere / Cylinder / Plane 这几种基础几何体；
/// 沿物体树把每个节点的 matrix（列主序 16 个数）乘下去，材质只取 color。其他几何体跳过，并在状态栏说明。
/// </summary>
public static class ThreeJsImporter
{
    public static bool LooksLikeThreeJs(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return IsThreeJs(document.RootElement, out _);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return false;
        }
    }

    static bool IsThreeJs(JsonElement root, out string reason)
    {
        reason = "";
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            reason = "这个 .json 不是 three.js 模型文件（没有 metadata）。";
            return false;
        }

        if (metadata.TryGetProperty("formatVersion", out _) && !metadata.TryGetProperty("version", out _))
        {
            reason = "这是旧版 three.js JSON（formatVersion 3），暂不支持，请在 three.js 里用 toJSON() 或导出 glTF。";
            return false;
        }

        if (!metadata.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetDouble() < 4 || version.GetDouble() >= 5)
        {
            reason = "这个 .json 不是 three.js 4.x 场景文件。";
            return false;
        }

        if (!root.TryGetProperty("object", out _) && !root.TryGetProperty("data", out _))
        {
            reason = "three.js 文件里没有 object（只有几何体或材质库），没东西可显示。";
            return false;
        }

        return true;
    }

    public static MeshData Load(string path)
    {
        using var stream = File.OpenRead(path);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"这个 .json 不是有效的 JSON：{ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (!IsThreeJs(root, out var reason))
            {
                throw new InvalidDataException(reason);
            }

            var context = new Context();
            if (root.TryGetProperty("geometries", out var geometries) && geometries.ValueKind == JsonValueKind.Array)
            {
                foreach (var geometry in geometries.EnumerateArray())
                {
                    if (geometry.TryGetProperty("uuid", out var uuid))
                    {
                        context.Geometries[uuid.GetString() ?? ""] = geometry;
                    }
                }
            }

            if (root.TryGetProperty("materials", out var materials) && materials.ValueKind == JsonValueKind.Array)
            {
                foreach (var material in materials.EnumerateArray())
                {
                    if (material.TryGetProperty("uuid", out var uuid))
                    {
                        context.Materials[uuid.GetString() ?? ""] = material;
                    }
                }
            }

            if (root.TryGetProperty("object", out var obj))
            {
                Walk(context, obj, Matrix4x4.Identity, -1);
            }
            else
            {
                // 单独的 BufferGeometry.toJSON()：{ metadata, type: "BufferGeometry", data: {...} }
                AddGeometry(context, root, Matrix4x4.Identity, Vector4.One, false);
            }

            var mesh = MeshImporter.Finish(Path.GetFileNameWithoutExtension(path), context.Vertices, context.Indices, context.Parts, []);
            if (context.Nodes.Count > 0)
            {
                mesh.Nodes = SceneNode.Link(context.Nodes);
            }

            if (context.World != null)
            {
                mesh.World = context.World;
            }

            if (context.Skipped.Count == 0)
            {
                return mesh;
            }

            var skipped = string.Join("、", context.Skipped.Distinct());
            return new MeshData
            {
                Name = mesh.Name,
                Vertices = mesh.Vertices,
                Indices = mesh.Indices,
                Min = mesh.Min,
                Max = mesh.Max,
                Parts = mesh.Parts,
                Textures = mesh.Textures,
                Notice = $"已跳过不支持的几何体：{skipped}",
                Nodes = mesh.Nodes,
                World = mesh.World,
            };
        }
    }

    sealed class Context
    {
        public readonly Dictionary<string, JsonElement> Geometries = new();
        public readonly Dictionary<string, JsonElement> Materials = new();
        public readonly List<Vertex> Vertices = [];
        public readonly List<uint> Indices = [];
        public readonly List<MeshPart> Parts = [];
        public readonly List<string> Skipped = [];
        public readonly List<SceneNode> Nodes = [];
        public SceneWorld? World;
    }

    static void Walk(Context context, JsonElement node, Matrix4x4 parentWorld, int parentSlot)
    {
        var local = Matrix4x4.Identity;
        if (node.TryGetProperty("matrix", out var matrix) && matrix.ValueKind == JsonValueKind.Array && matrix.GetArrayLength() == 16)
        {
            // three.js 的 matrix.elements 是列主序（列向量约定）；按顺序填进 System.Numerics（行向量约定）正好就是它的转置，含义相同。
            Span<float> e = stackalloc float[16];
            int i = 0;
            foreach (var value in matrix.EnumerateArray())
            {
                e[i++] = value.GetSingle();
            }

            local = new Matrix4x4(e[0], e[1], e[2], e[3], e[4], e[5], e[6], e[7], e[8], e[9], e[10], e[11], e[12], e[13], e[14], e[15]);
        }

        var world = local * parentWorld;
        bool visible = !node.TryGetProperty("visible", out var v) || v.ValueKind != JsonValueKind.False;
        if (!visible)
        {
            return;
        }

        string type = node.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        int firstPart = context.Parts.Count;
        if (node.TryGetProperty("geometry", out var geometryId) && geometryId.ValueKind == JsonValueKind.String)
        {
            if (type is "Mesh" or "SkinnedMesh" or "InstancedMesh")
            {
                var (color, doubleSided) = ReadMaterial(context, node);
                if (context.Geometries.TryGetValue(geometryId.GetString() ?? "", out var geometry))
                {
                    AddGeometry(context, geometry, world, color, doubleSided);
                }
            }
            else if (type is "Line" or "LineSegments" or "LineLoop" or "Points")
            {
                context.Skipped.Add(type);
            }
        }

        if (type is "AmbientLight" or "HemisphereLight")
        {
            context.World = new SceneWorld { Color = ReadColor(node), Strength = Num(node, "intensity", 1), FromFile = true };
        }

        int slot = context.Nodes.Count;
        string name = node.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
        context.Nodes.Add(new SceneNode
        {
            Index = slot,
            Name = name != "" ? name : type != "" ? type : $"Node {slot}",
            Parent = parentSlot,
            Kind = context.Parts.Count > firstPart ? SceneNodeKind.Mesh
                : type.EndsWith("Camera", StringComparison.Ordinal) ? SceneNodeKind.Camera
                : type is "DirectionalLight" or "PointLight" or "SpotLight" or "RectAreaLight" ? SceneNodeKind.Light
                : SceneNodeKind.Empty,
            Local = local,
            World = world,
            PartIndices = Enumerable.Range(firstPart, context.Parts.Count - firstPart).ToArray(),
            Camera = ReadCamera(node, type),
            Light = ReadLight(node, type),
        });

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(context, child, world, slot);
            }
        }
    }

    static SceneCamera? ReadCamera(JsonElement node, string type) => type switch
    {
        "PerspectiveCamera" => new SceneCamera
        {
            YFov = Num(node, "fov", 50) * MathF.PI / 180f,
            AspectRatio = Num(node, "aspect", 1),
            Near = Num(node, "near", 0.1f),
            Far = Num(node, "far", 2000),
        },
        "OrthographicCamera" => new SceneCamera
        {
            Orthographic = true,
            OrthoHalfHeight = (Num(node, "top", 1) - Num(node, "bottom", -1)) * 0.5f,
            Near = Num(node, "near", 0.1f),
            Far = Num(node, "far", 2000),
        },
        _ => null,
    };

    static Vector3 ReadColor(JsonElement node)
    {
        if (node.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Number)
        {
            int rgb = c.GetInt32();
            return new Vector3(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
        }

        return Vector3.One;
    }

    /// <summary>three.js r155 以后按物理单位：点光 / 聚光为 cd，平行光为 lux，面光为 nit；按 Blender glTF 导入器的系数换成 W、W/m²。</summary>
    static SceneLight? ReadLight(JsonElement node, string type)
    {
        float intensity = Num(node, "intensity", 1);
        var color = ReadColor(node);
        switch (type)
        {
            case "DirectionalLight":
                return new SceneLight { Type = SceneLightType.Directional, Color = color, Intensity = intensity, Power = SceneLight.LuxToWattsPerSquareMeter(intensity) };
            case "PointLight":
                return new SceneLight { Type = SceneLightType.Point, Color = color, Intensity = intensity, Power = SceneLight.CandelaToWatts(intensity), Range = Num(node, "distance", 0), Radius = SceneLight.DefaultRadius };
            case "SpotLight":
                float outer = Num(node, "angle", MathF.PI / 3);
                float blend = Math.Clamp(Num(node, "penumbra", 0), 0, 1);
                return new SceneLight
                {
                    Type = SceneLightType.Spot,
                    Color = color,
                    Intensity = intensity,
                    Power = SceneLight.CandelaToWatts(intensity),
                    Range = Num(node, "distance", 0),
                    Radius = SceneLight.DefaultRadius,
                    OuterConeAngle = outer,
                    InnerConeAngle = outer * (1 - blend),
                    SpotBlend = blend,
                };
            case "RectAreaLight":
                float w = Num(node, "width", 10), h = Num(node, "height", 10);
                return new SceneLight
                {
                    Type = SceneLightType.Area,
                    Color = color,
                    Intensity = intensity,
                    // nit（cd/m²）× 面积 × π → lm，再除以 683。
                    Power = intensity * w * h * MathF.PI / SceneLight.WattsToLumens,
                    Shape = AreaShape.Rectangle,
                    Size = new Vector2(w, h),
                };
            default:
                return null;
        }
    }

    static (Vector4 Color, bool DoubleSided) ReadMaterial(Context context, JsonElement node)
    {
        if (!node.TryGetProperty("material", out var reference))
        {
            return (Vector4.One, false);
        }

        // 多材质时（数组）只取第一个的颜色。
        var id = reference.ValueKind == JsonValueKind.Array
            ? reference.EnumerateArray().FirstOrDefault().GetString()
            : reference.ValueKind == JsonValueKind.String ? reference.GetString() : null;
        if (id == null || !context.Materials.TryGetValue(id, out var material))
        {
            return (Vector4.One, false);
        }

        var color = Vector4.One;
        if (material.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Number)
        {
            int rgb = c.GetInt32();
            color = new Vector4(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
            // three.js r152 以后材质颜色按线性空间存储；这里的着色器不做 sRGB 转换，直接把线性值转回 sRGB 显示。
            color = new Vector4(LinearToSrgb(color.X), LinearToSrgb(color.Y), LinearToSrgb(color.Z), 1f);
        }

        bool doubleSided = material.TryGetProperty("side", out var side) && side.ValueKind == JsonValueKind.Number && side.GetInt32() == 2;
        return (color, doubleSided);
    }

    static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    static void AddGeometry(Context context, JsonElement geometry, Matrix4x4 world, Vector4 color, bool doubleSided)
    {
        string type = geometry.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        List<Vertex> vertices;
        List<uint> indices;
        switch (type)
        {
            case "BufferGeometry":
                if (!ReadBufferGeometry(geometry, out vertices, out indices))
                {
                    context.Skipped.Add("BufferGeometry（缺 position 或用了 interleaved 缓冲）");
                    return;
                }

                break;
            case "BoxGeometry":
            case "BoxBufferGeometry":
                (vertices, indices) = Primitives.Box(Num(geometry, "width", 1), Num(geometry, "height", 1), Num(geometry, "depth", 1));
                break;
            case "SphereGeometry":
            case "SphereBufferGeometry":
                (vertices, indices) = Primitives.Sphere(Num(geometry, "radius", 1), (int)Num(geometry, "widthSegments", 32), (int)Num(geometry, "heightSegments", 16));
                break;
            case "CylinderGeometry":
            case "CylinderBufferGeometry":
                (vertices, indices) = Primitives.Cylinder(
                    Num(geometry, "radiusTop", 1), Num(geometry, "radiusBottom", 1), Num(geometry, "height", 1),
                    (int)Num(geometry, "radialSegments", 32), geometry.TryGetProperty("openEnded", out var open) && open.ValueKind == JsonValueKind.True);
                break;
            case "PlaneGeometry":
            case "PlaneBufferGeometry":
                (vertices, indices) = Primitives.Plane(Num(geometry, "width", 1), Num(geometry, "height", 1));
                doubleSided = true;
                break;
            default:
                context.Skipped.Add(type == "" ? "未知类型" : type);
                return;
        }

        Matrix4x4.Invert(world, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        bool flip = world.GetDeterminant() < 0;
        uint baseIndex = (uint)context.Vertices.Count;
        uint packedTint = Vertex.PackColor(color);
        foreach (var vertex in vertices)
        {
            var transformed = vertex;
            transformed.Position = Vector3.Transform(vertex.Position, world);
            var normal = Vector3.TransformNormal(vertex.Normal, normalMatrix);
            transformed.Normal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitY;
            transformed.Color = vertex.Color == 0xFFFFFFFF ? packedTint : Multiply(vertex.Color, color);
            context.Vertices.Add(transformed);
        }

        int start = context.Indices.Count;
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            context.Indices.Add(baseIndex + indices[i]);
            context.Indices.Add(baseIndex + (flip ? indices[i + 2] : indices[i + 1]));
            context.Indices.Add(baseIndex + (flip ? indices[i + 1] : indices[i + 2]));
        }

        context.Parts.Add(new MeshPart { StartIndex = start, IndexCount = context.Indices.Count - start, DoubleSided = doubleSided });
    }

    static uint Multiply(uint packed, Vector4 tint)
    {
        var c = new Vector4(packed & 255, (packed >> 8) & 255, (packed >> 16) & 255, (packed >> 24) & 255) / 255f;
        return Vertex.PackColor(c * tint);
    }

    static float Num(JsonElement element, string name, float fallback)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetSingle() : fallback;

    static bool ReadBufferGeometry(JsonElement geometry, out List<Vertex> vertices, out List<uint> indices)
    {
        vertices = [];
        indices = [];
        if (!geometry.TryGetProperty("data", out var data) || !data.TryGetProperty("attributes", out var attributes))
        {
            return false;
        }

        var positions = ReadAttribute(attributes, "position", out int positionSize);
        if (positions == null || positionSize < 3)
        {
            return false;
        }

        int count = positions.Length / positionSize;
        var normals = ReadAttribute(attributes, "normal", out int normalSize);
        var uvs = ReadAttribute(attributes, "uv", out int uvSize);
        var colors = ReadAttribute(attributes, "color", out int colorSize);
        for (int i = 0; i < count; i++)
        {
            var vertex = new Vertex(new Vector3(positions[i * positionSize], positions[i * positionSize + 1], positions[i * positionSize + 2]), Vector3.Zero);
            if (normals != null && normalSize >= 3 && (i + 1) * normalSize <= normals.Length)
            {
                vertex.Normal = new Vector3(normals[i * normalSize], normals[i * normalSize + 1], normals[i * normalSize + 2]);
            }

            if (uvs != null && uvSize >= 2 && (i + 1) * uvSize <= uvs.Length)
            {
                // three.js 的 uv 原点在左下，D3D 在左上。
                vertex.TexCoord = new Vector2(uvs[i * uvSize], 1f - uvs[i * uvSize + 1]);
            }

            if (colors != null && colorSize >= 3 && (i + 1) * colorSize <= colors.Length)
            {
                vertex.Color = Vertex.PackColor(new Vector4(colors[i * colorSize], colors[i * colorSize + 1], colors[i * colorSize + 2], colorSize >= 4 ? colors[i * colorSize + 3] : 1f));
            }

            vertices.Add(vertex);
        }

        if (data.TryGetProperty("index", out var index) && index.TryGetProperty("array", out var indexArray) && indexArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in indexArray.EnumerateArray())
            {
                uint i = value.GetUInt32();
                if (i >= count)
                {
                    throw new InvalidDataException("three.js 几何体的索引超出了顶点数量。");
                }

                indices.Add(i);
            }
        }
        else
        {
            for (uint i = 0; i < count; i++)
            {
                indices.Add(i);
            }
        }

        indices.RemoveRange(indices.Count - indices.Count % 3, indices.Count % 3);
        if (normals == null)
        {
            ComputeNormals(vertices, indices);
        }

        return indices.Count >= 3;
    }

    static float[]? ReadAttribute(JsonElement attributes, string name, out int itemSize)
    {
        itemSize = 0;
        if (!attributes.TryGetProperty(name, out var attribute) || attribute.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (attribute.TryGetProperty("isInterleavedBufferAttribute", out _) || !attribute.TryGetProperty("array", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        itemSize = (int)Num(attribute, "itemSize", 3);
        var result = new float[array.GetArrayLength()];
        int i = 0;
        foreach (var value in array.EnumerateArray())
        {
            result[i++] = value.GetSingle();
        }

        // normalized 的整数属性（如 Uint8 颜色）还原到 0–1。
        if (attribute.TryGetProperty("normalized", out var normalized) && normalized.ValueKind == JsonValueKind.True
            && attribute.TryGetProperty("type", out var typeName))
        {
            float scale = typeName.GetString() switch
            {
                "Uint8Array" => 1f / 255f,
                "Uint16Array" => 1f / 65535f,
                "Int8Array" => 1f / 127f,
                "Int16Array" => 1f / 32767f,
                _ => 1f,
            };
            for (int k = 0; k < result.Length; k++)
            {
                result[k] *= scale;
            }
        }

        return result;
    }

    internal static void ComputeNormals(List<Vertex> vertices, List<uint> indices)
    {
        var normals = new Vector3[vertices.Count];
        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            var a = vertices[(int)indices[i]].Position;
            var b = vertices[(int)indices[i + 1]].Position;
            var c = vertices[(int)indices[i + 2]].Position;
            var n = Vector3.Cross(b - a, c - a);
            normals[indices[i]] += n;
            normals[indices[i + 1]] += n;
            normals[indices[i + 2]] += n;
        }

        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.Normal = normals[i].LengthSquared() > 0 ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
            vertices[i] = vertex;
        }
    }

    /// <summary>和 three.js 同名几何体一致的生成方式（中心在原点，Y 轴朝上，逆时针为正面）。</summary>
    static class Primitives
    {
        public static (List<Vertex>, List<uint>) Box(float width, float height, float depth)
        {
            var vertices = new List<Vertex>();
            var indices = new List<uint>();
            var h = new Vector3(width, height, depth) * 0.5f;
            Face(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
            Face(-Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ);
            Face(Vector3.UnitY, Vector3.UnitZ, Vector3.UnitX);
            Face(-Vector3.UnitY, -Vector3.UnitZ, Vector3.UnitX);
            Face(Vector3.UnitZ, Vector3.UnitY, -Vector3.UnitX);
            Face(-Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
            return (vertices, indices);

            void Face(Vector3 normal, Vector3 up, Vector3 right)
            {
                // right × up = normal 保证逆时针朝外。
                right = Vector3.Cross(up, normal);
                uint start = (uint)vertices.Count;
                var center = normal * h;
                var u = right * h;
                var v = up * h;
                Add(center - u - v, new Vector2(0, 1));
                Add(center + u - v, new Vector2(1, 1));
                Add(center + u + v, new Vector2(1, 0));
                Add(center - u + v, new Vector2(0, 0));
                indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);

                void Add(Vector3 p, Vector2 uv) => vertices.Add(new Vertex(p, normal) { TexCoord = uv });
            }
        }

        public static (List<Vertex>, List<uint>) Sphere(float radius, int widthSegments, int heightSegments)
        {
            widthSegments = Math.Clamp(widthSegments, 3, 512);
            heightSegments = Math.Clamp(heightSegments, 2, 512);
            var vertices = new List<Vertex>();
            var indices = new List<uint>();
            for (int y = 0; y <= heightSegments; y++)
            {
                float v = y / (float)heightSegments;
                float theta = v * MathF.PI;
                for (int x = 0; x <= widthSegments; x++)
                {
                    float u = x / (float)widthSegments;
                    float phi = u * MathF.PI * 2f;
                    var n = new Vector3(-MathF.Cos(phi) * MathF.Sin(theta), MathF.Cos(theta), MathF.Sin(phi) * MathF.Sin(theta));
                    vertices.Add(new Vertex(n * radius, n) { TexCoord = new Vector2(u, v) });
                }
            }

            int row = widthSegments + 1;
            for (int y = 0; y < heightSegments; y++)
            {
                for (int x = 0; x < widthSegments; x++)
                {
                    uint a = (uint)(y * row + x + 1);
                    uint b = (uint)(y * row + x);
                    uint c = (uint)((y + 1) * row + x);
                    uint d = (uint)((y + 1) * row + x + 1);
                    if (y != 0)
                    {
                        indices.AddRange([a, b, d]);
                    }

                    if (y != heightSegments - 1)
                    {
                        indices.AddRange([b, c, d]);
                    }
                }
            }

            return (vertices, indices);
        }

        public static (List<Vertex>, List<uint>) Cylinder(float radiusTop, float radiusBottom, float height, int radialSegments, bool openEnded)
        {
            radialSegments = Math.Clamp(radialSegments, 3, 512);
            var vertices = new List<Vertex>();
            var indices = new List<uint>();
            float half = height * 0.5f;
            float slope = (radiusBottom - radiusTop) / MathF.Max(height, 1e-6f);
            for (int y = 0; y <= 1; y++)
            {
                float radius = y == 0 ? radiusTop : radiusBottom;
                float py = y == 0 ? half : -half;
                for (int x = 0; x <= radialSegments; x++)
                {
                    float u = x / (float)radialSegments;
                    float angle = u * MathF.PI * 2f;
                    float sin = MathF.Sin(angle);
                    float cos = MathF.Cos(angle);
                    var normal = Vector3.Normalize(new Vector3(sin, slope, cos));
                    vertices.Add(new Vertex(new Vector3(radius * sin, py, radius * cos), normal) { TexCoord = new Vector2(u, y) });
                }
            }

            int row = radialSegments + 1;
            for (int x = 0; x < radialSegments; x++)
            {
                uint a = (uint)x;
                uint b = (uint)(row + x);
                uint c = (uint)(row + x + 1);
                uint d = (uint)(x + 1);
                indices.AddRange([a, b, d, b, c, d]);
            }

            if (!openEnded)
            {
                Cap(true);
                Cap(false);
            }

            return (vertices, indices);

            void Cap(bool top)
            {
                float radius = top ? radiusTop : radiusBottom;
                if (radius <= 0)
                {
                    return;
                }

                float sign = top ? 1f : -1f;
                var normal = new Vector3(0, sign, 0);
                uint center = (uint)vertices.Count;
                vertices.Add(new Vertex(new Vector3(0, half * sign, 0), normal) { TexCoord = new Vector2(0.5f, 0.5f) });
                for (int x = 0; x <= radialSegments; x++)
                {
                    float angle = x / (float)radialSegments * MathF.PI * 2f;
                    vertices.Add(new Vertex(new Vector3(radius * MathF.Sin(angle), half * sign, radius * MathF.Cos(angle)), normal));
                }

                for (int x = 0; x < radialSegments; x++)
                {
                    uint i = center + 1 + (uint)x;
                    if (top)
                    {
                        indices.AddRange([center, i, i + 1]);
                    }
                    else
                    {
                        indices.AddRange([center, i + 1, i]);
                    }
                }
            }
        }

        public static (List<Vertex>, List<uint>) Plane(float width, float height)
        {
            var n = Vector3.UnitZ;
            float w = width * 0.5f;
            float h = height * 0.5f;
            var vertices = new List<Vertex>
            {
                new(new Vector3(-w, -h, 0), n) { TexCoord = new Vector2(0, 1) },
                new(new Vector3(w, -h, 0), n) { TexCoord = new Vector2(1, 1) },
                new(new Vector3(w, h, 0), n) { TexCoord = new Vector2(1, 0) },
                new(new Vector3(-w, h, 0), n) { TexCoord = new Vector2(0, 0) },
            };
            return (vertices, [0, 1, 2, 0, 2, 3]);
        }
    }
}
