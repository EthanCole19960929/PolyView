using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using AssimpScene = Silk.NET.Assimp.Scene;

namespace PolyView;

/// <summary>
/// 用 Assimp（Silk.NET.Assimp，原生 assimp 6.x）读取 FBX、DAE、OBJ(+MTL)、3DS、PLY 等格式。
/// 自己遍历 aiNode 树把节点变换烘焙进顶点（保留节点层级给大纲视图），缺法线时生成面法线，蒙皮模型按绑定姿态显示。
/// </summary>
public static unsafe class AssimpImporter
{
    static readonly Lazy<Assimp> Api = new(Assimp.GetApi);

    const PostProcessSteps Steps =
        PostProcessSteps.Triangulate
        | PostProcessSteps.GenerateNormals
        | PostProcessSteps.JoinIdenticalVertices
        | PostProcessSteps.SortByPrimitiveType
        | PostProcessSteps.FlipUVs;

    public static MeshData Load(string path)
    {
        var assimp = Api.Value;
        AssimpScene* scene = assimp.ImportFile(path, (uint)Steps);
        if (scene == null || scene->MRootNode == null)
        {
            var reason = assimp.GetErrorStringS();
            throw new InvalidDataException(string.IsNullOrWhiteSpace(reason) ? "Assimp 读取失败。" : $"Assimp 读取失败：{reason}");
        }

        try
        {
            return Convert(assimp, scene, path);
        }
        finally
        {
            assimp.ReleaseImport(scene);
        }
    }

    static MeshData Convert(Assimp assimp, AssimpScene* scene, string path)
    {
        var vertices = new List<Vertex>();
        var indices = new List<uint>();
        var parts = new List<MeshPart>();
        var textures = new List<MeshTexture>();
        var materials = new (Vector4 Color, int Texture, bool DoubleSided)[scene->MNumMaterials];
        var textureCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? "";
        for (int i = 0; i < materials.Length; i++)
        {
            materials[i] = ReadMaterial(assimp, scene, scene->MMaterials[i], folder, textures, textureCache);
        }

        var cameras = new Dictionary<string, nint>();
        for (uint i = 0; i < scene->MNumCameras; i++)
        {
            cameras.TryAdd(scene->MCameras[i]->MName.AsString, (nint)scene->MCameras[i]);
        }

        var lights = new Dictionary<string, nint>();
        for (uint i = 0; i < scene->MNumLights; i++)
        {
            lights.TryAdd(scene->MLights[i]->MName.AsString, (nint)scene->MLights[i]);
        }

        var nodes = new List<SceneNode>();
        var stack = new Stack<(nint Node, int Parent, Matrix4x4 ParentWorld)>();
        stack.Push(((nint)scene->MRootNode, -1, Matrix4x4.Identity));
        while (stack.Count > 0)
        {
            var (nodePtr, parent, parentWorld) = stack.Pop();
            var node = (Node*)nodePtr;
            // Assimp 是列向量约定，System.Numerics 是行向量约定，要转置。
            var local = Matrix4x4.Transpose(node->MTransformation);
            var world = local * parentWorld;
            int firstPart = parts.Count;
            for (uint i = 0; i < node->MNumMeshes; i++)
            {
                uint meshIndex = node->MMeshes[i];
                if (meshIndex < scene->MNumMeshes)
                {
                    AddMesh(scene->MMeshes[meshIndex], world, materials, vertices, indices, parts);
                }
            }

            string name = node->MName.AsString;
            var camera = cameras.TryGetValue(name, out var c) ? ReadCamera((Silk.NET.Assimp.Camera*)c) : null;
            var light = lights.TryGetValue(name, out var l) ? ReadLight((Light*)l) : null;
            int slot = nodes.Count;
            nodes.Add(new SceneNode
            {
                Index = slot,
                Name = string.IsNullOrEmpty(name) ? $"Node {slot}" : name,
                Parent = parent,
                Kind = parts.Count > firstPart ? SceneNodeKind.Mesh : camera != null ? SceneNodeKind.Camera : light != null ? SceneNodeKind.Light : SceneNodeKind.Empty,
                Local = local,
                World = world,
                PartIndices = Enumerable.Range(firstPart, parts.Count - firstPart).ToArray(),
                Camera = camera,
                Light = light,
            });

            for (int i = (int)node->MNumChildren - 1; i >= 0; i--)
            {
                stack.Push(((nint)node->MChildren[i], slot, world));
            }
        }

        var result = MeshImporter.Finish(Path.GetFileNameWithoutExtension(path), vertices, indices, parts, textures);
        result.Nodes = SceneNode.Link(nodes);
        return result;
    }

    static void AddMesh(
        AssimpMesh* mesh,
        Matrix4x4 world,
        (Vector4 Color, int Texture, bool DoubleSided)[] materials,
        List<Vertex> vertices,
        List<uint> indices,
        List<MeshPart> parts)
    {
        if (((PrimitiveType)mesh->MPrimitiveTypes & PrimitiveType.Triangle) == 0 || mesh->MNumVertices == 0)
        {
            return;
        }

        Matrix4x4.Invert(world, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        bool flip = world.GetDeterminant() < 0;
        var material = mesh->MMaterialIndex < materials.Length
            ? materials[mesh->MMaterialIndex]
            : (Color: Vector4.One, Texture: -1, DoubleSided: false);
        uint baseVertex = (uint)vertices.Count;
        var uv = mesh->MTextureCoords[0];
        var colors = mesh->MColors[0];
        for (uint v = 0; v < mesh->MNumVertices; v++)
        {
            var normal = mesh->MNormals != null ? Vector3.TransformNormal(mesh->MNormals[v], normalMatrix) : Vector3.UnitY;
            if (normal.LengthSquared() > 1e-20f)
            {
                normal = Vector3.Normalize(normal);
            }

            var color = material.Color;
            if (colors != null)
            {
                color *= colors[v];
            }

            vertices.Add(new Vertex(Vector3.Transform(mesh->MVertices[v], world), normal)
            {
                TexCoord = uv != null ? new Vector2(uv[v].X, uv[v].Y) : Vector2.Zero,
                Color = Vertex.PackColor(color),
            });
        }

        int start = indices.Count;
        for (uint f = 0; f < mesh->MNumFaces; f++)
        {
            var face = mesh->MFaces[f];
            if (face.MNumIndices != 3)
            {
                continue;
            }

            indices.Add(baseVertex + face.MIndices[0]);
            indices.Add(baseVertex + face.MIndices[flip ? 2 : 1]);
            indices.Add(baseVertex + face.MIndices[flip ? 1 : 2]);
        }

        if (indices.Count > start)
        {
            parts.Add(new MeshPart
            {
                StartIndex = start,
                IndexCount = indices.Count - start,
                TextureIndex = material.Texture,
                DoubleSided = material.DoubleSided,
            });
        }
    }

    static SceneCamera ReadCamera(Silk.NET.Assimp.Camera* camera)
    {
        // Assimp 的 mHorizontalFOV 和 mOrthographicWidth 都是“半”值。
        float aspect = camera->MAspect;
        float halfH = camera->MHorizontalFOV;
        bool ortho = camera->MOrthographicWidth > 0;
        return new SceneCamera
        {
            Orthographic = ortho,
            YFov = ortho ? 0 : aspect > 0 ? 2f * MathF.Atan(MathF.Tan(halfH) / aspect) : 2f * halfH,
            OrthoHalfHeight = ortho ? (aspect > 0 ? camera->MOrthographicWidth / aspect : camera->MOrthographicWidth) : 0,
            AspectRatio = aspect,
            Near = camera->MClipPlaneNear,
            Far = camera->MClipPlaneFar,
        };
    }

    static SceneLight ReadLight(Light* light)
    {
        // Assimp 把亮度乘进了颜色，这里拆成归一化颜色 + 亮度。
        var color = light->MColorDiffuse;
        float intensity = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        return new SceneLight
        {
            Type = light->MType switch
            {
                LightSourceType.Directional => SceneLightType.Directional,
                LightSourceType.Point => SceneLightType.Point,
                LightSourceType.Spot => SceneLightType.Spot,
                LightSourceType.Ambient => SceneLightType.Ambient,
                LightSourceType.Area => SceneLightType.Area,
                _ => SceneLightType.Unknown,
            },
            Color = intensity > 0 ? color / intensity : Vector3.One,
            Intensity = intensity,
            InnerConeAngle = light->MAngleInnerCone * 0.5f,
            OuterConeAngle = light->MAngleOuterCone * 0.5f,
        };
    }

    static (Vector4 Color, int Texture, bool DoubleSided) ReadMaterial(
        Assimp assimp,
        AssimpScene* scene,
        Material* material,
        string folder,
        List<MeshTexture> textures,
        Dictionary<string, int> cache)
    {
        var color = Vector4.One;
        Vector4 value = default;
        if (assimp.GetMaterialColor(material, "$clr.base", 0, 0, ref value) == Return.Success
            || assimp.GetMaterialColor(material, Assimp.MaterialColorDiffuseBase, 0, 0, ref value) == Return.Success)
        {
            color = new Vector4(value.X, value.Y, value.Z, 1f);
        }

        int twoSided = 0;
        uint max = 1;
        assimp.GetMaterialIntegerArray(material, Assimp.MaterialTwosidedBase, 0, 0, ref twoSided, ref max);

        int texture = -1;
        AssimpString file = default;
        if (assimp.GetMaterialTexture(material, TextureType.BaseColor, 0, ref file, null, null, null, null, null, null) == Return.Success
            || assimp.GetMaterialTexture(material, TextureType.Diffuse, 0, ref file, null, null, null, null, null, null) == Return.Success)
        {
            texture = LoadTexture(assimp, scene, file.AsString, folder, textures, cache);
            if (texture >= 0)
            {
                // 有贴图时 FBX/OBJ 的漫反射颜色常常是灰色占位，直接用贴图颜色。
                color = Vector4.One;
            }
        }

        return (color, texture, twoSided != 0);
    }

    static int LoadTexture(Assimp assimp, AssimpScene* scene, string reference, string folder, List<MeshTexture> textures, Dictionary<string, int> cache)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return -1;
        }

        if (cache.TryGetValue(reference, out int cached))
        {
            return cached;
        }

        MeshTexture? decoded = null;
        Texture* embedded = FindEmbeddedTexture(scene, reference);
        if (embedded != null)
        {
            if (embedded->MHeight == 0)
            {
                var bytes = new ReadOnlySpan<byte>(embedded->PcData, (int)embedded->MWidth).ToArray();
                decoded = TextureDecoder.Decode(bytes);
            }
        }
        else
        {
            var file = ResolveTexturePath(reference, folder);
            if (file != null)
            {
                decoded = TextureDecoder.Decode(System.IO.File.ReadAllBytes(file));
            }
        }

        int slot = -1;
        if (decoded != null)
        {
            slot = textures.Count;
            textures.Add(decoded);
        }

        cache[reference] = slot;
        return slot;
    }

    /// <summary>
    /// 原生库里没有导出 aiGetEmbeddedTexture，这里按同样规则自己找：
    /// "*N" 表示第 N 张内嵌贴图，否则按文件名匹配内嵌贴图的 mFilename。
    /// </summary>
    static Texture* FindEmbeddedTexture(AssimpScene* scene, string reference)
    {
        if (scene->MNumTextures == 0)
        {
            return null;
        }

        if (reference.StartsWith('*') && int.TryParse(reference.AsSpan(1), out int index))
        {
            return index >= 0 && index < scene->MNumTextures ? scene->MTextures[index] : null;
        }

        var name = Path.GetFileName(reference.Replace('\\', '/'));
        for (uint i = 0; i < scene->MNumTextures; i++)
        {
            var texture = scene->MTextures[i];
            var embeddedName = Path.GetFileName(texture->MFilename.AsString.Replace('\\', '/'));
            if (embeddedName.Length > 0 && embeddedName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return texture;
            }
        }

        return null;
    }

    static string? ResolveTexturePath(string reference, string folder)
    {
        var cleaned = reference.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            Path.IsPathRooted(cleaned) ? cleaned : Path.Combine(folder, cleaned),
            Path.Combine(folder, Path.GetFileName(cleaned)),
        };
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }
}
