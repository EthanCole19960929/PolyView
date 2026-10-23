using System.IO;
using System.Numerics;
using Silk.NET.Assimp;
using AssimpMesh = Silk.NET.Assimp.Mesh;
using AssimpScene = Silk.NET.Assimp.Scene;

namespace PolyView;

/// <summary>
/// 用 Assimp（Silk.NET.Assimp，原生 assimp 6.x）读取 FBX、DAE、OBJ(+MTL)、3DS、PLY 等格式。
/// 节点变换直接烘焙进顶点（PreTransformVertices），缺法线时生成面法线，蒙皮模型按绑定姿态显示。
/// </summary>
public static unsafe class AssimpImporter
{
    static readonly Lazy<Assimp> Api = new(Assimp.GetApi);

    const PostProcessSteps Steps =
        PostProcessSteps.Triangulate
        | PostProcessSteps.GenerateNormals
        | PostProcessSteps.JoinIdenticalVertices
        | PostProcessSteps.PreTransformVertices
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

        for (uint m = 0; m < scene->MNumMeshes; m++)
        {
            AssimpMesh* mesh = scene->MMeshes[m];
            if (((PrimitiveType)mesh->MPrimitiveTypes & PrimitiveType.Triangle) == 0 || mesh->MNumVertices == 0)
            {
                continue;
            }

            var material = mesh->MMaterialIndex < materials.Length
                ? materials[mesh->MMaterialIndex]
                : (Color: Vector4.One, Texture: -1, DoubleSided: false);
            uint baseVertex = (uint)vertices.Count;
            var uv = mesh->MTextureCoords[0];
            var colors = mesh->MColors[0];
            for (uint v = 0; v < mesh->MNumVertices; v++)
            {
                var normal = mesh->MNormals != null ? mesh->MNormals[v] : Vector3.UnitY;
                if (normal.LengthSquared() > 1e-20f)
                {
                    normal = Vector3.Normalize(normal);
                }

                var color = material.Color;
                if (colors != null)
                {
                    color *= colors[v];
                }

                vertices.Add(new Vertex(mesh->MVertices[v], normal)
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
                indices.Add(baseVertex + face.MIndices[1]);
                indices.Add(baseVertex + face.MIndices[2]);
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

        return MeshImporter.Finish(Path.GetFileNameWithoutExtension(path), vertices, indices, parts, textures);
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
