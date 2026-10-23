using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Silk.NET.Assimp;
using AiMaterial = Silk.NET.Assimp.Material;
using AiMesh = Silk.NET.Assimp.Mesh;
using AiNode = Silk.NET.Assimp.Node;
using AiScene = Silk.NET.Assimp.Scene;
using AiTexture = Silk.NET.Assimp.Texture;
using File = System.IO.File;

namespace PolyView.Export;

/// <summary>
/// Writes scene objects to FBX / OBJ / glTF / GLB / STL / PLY / DAE through Assimp (Silk.NET.Assimp).
/// <para>
/// Space: the importer bakes node transforms into the vertices and the viewport recentres them by ImportOffset,
/// so a stored vertex v is at <c>v * object.World</c> in the scene. Node hierarchy is rebuilt from MeshData.Nodes:
/// each object becomes a node with <c>T(-ImportOffset) * object.World</c>, file nodes keep their Local matrices,
/// and vertices are moved back into node space with <c>T(ImportOffset) * inverse(node.World)</c>, so nothing is applied twice.
/// STL / PLY have no hierarchy: vertices are baked to world space (and turned Z-up, see ExportOptions).
/// </para>
/// PolyView is Y-up, right-handed, metres. glTF / OBJ / DAE (Y_UP, meter) are written as is; FBX is patched to
/// UnitScaleFactor = 100 (metres) with Y-up axis metadata so Blender opens it upright at 1:1.
/// </summary>
public static unsafe class SceneExporter
{
    static readonly Lazy<Assimp> Api = new(Assimp.GetApi);

    sealed class XNode
    {
        public required string Name;
        public Matrix4x4 Local = Matrix4x4.Identity;
        public readonly List<int> Meshes = [];
        public readonly List<XNode> Children = [];
    }

    sealed class XMesh
    {
        public required string Name;
        public required Vector3[] Positions;
        public required Vector3[] Normals;
        public Vector3[]? TexCoords;
        public Vector4[]? Colors;
        public required uint[] Indices;
        public int Material;
    }

    sealed record XMaterial(string Name, Vector4 Color, int Texture, bool DoubleSided);

    sealed class Builder
    {
        public readonly List<XMesh> Meshes = [];
        public readonly List<XMaterial> Materials = [];
        public readonly Dictionary<(Vector4, int, bool), int> MaterialLookup = [];
        public readonly List<(byte[] Png, string FileName)> Textures = [];
        public readonly Dictionary<(int ObjectId, int Texture), int> TextureLookup = [];
        public readonly HashSet<string> NodeNames = new(StringComparer.Ordinal);
        public int Vertices;
        public int Triangles;
        public Vector3 Min = new(float.MaxValue);
        public Vector3 Max = new(float.MinValue);
    }

    /// <summary>Export the given objects. Throws ExportException with a readable message on failure.</summary>
    public static ExportResult Export(IReadOnlyList<SceneObject> objects, string path, string? format = null, ExportOptions? options = null)
    {
        options ??= ExportOptions.Default;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ExportException(UiText.ExportNoPath);
        }

        var fmt = ExportFormat.Find(format) ?? ExportFormat.FromPath(path)
            ?? throw new ExportException(UiText.ExportUnknownFormat(format ?? Path.GetExtension(path)));
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(path), fmt.Extension, StringComparison.OrdinalIgnoreCase))
        {
            path = Path.ChangeExtension(path, fmt.Extension);
        }

        var list = objects.Where(o => !options.SkipHidden || o.Visible).ToList();
        if (list.Count == 0)
        {
            throw new ExportException(UiText.ExportNothing);
        }

        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);
        string baseName = Path.GetFileNameWithoutExtension(path);
        bool flat = fmt.Key is "stl" or "ply";
        var axis = flat && options.ZUpForStlPly ? Matrix4x4.CreateRotationX(MathF.PI / 2f) : Matrix4x4.Identity;
        bool embedTextures = fmt.Key == "glb";
        bool textures = options.IncludeTextures && !flat;

        var b = new Builder();
        var root = new XNode { Name = "RootNode" };
        b.NodeNames.Add(root.Name);
        foreach (var o in list)
        {
            AddObject(b, root, o, flat, axis, textures, options);
        }

        if (b.Meshes.Count == 0)
        {
            throw new ExportException(UiText.ExportNothing);
        }

        if (b.Materials.Count == 0)
        {
            b.Materials.Add(new XMaterial("Default", Vector4.One, -1, false));
        }

        var extra = new List<string>();
        if (!embedTextures)
        {
            foreach (var (png, name) in b.Textures)
            {
                var file = Path.Combine(folder, name);
                File.WriteAllBytes(file, png);
                extra.Add(file);
            }
        }

        var tmp = path + ".tmp" + fmt.Extension;
        var natives = new List<nint>();
        try
        {
            var scene = BuildScene(b, root, embedTextures, natives);
            var assimp = Api.Value;
            // Write to a temp name first so a failed export never leaves a half-written file at the target.
            var ret = assimp.ExportScene(scene, fmt.AssimpId, tmp, 0);
            if (ret != Return.Success || !File.Exists(tmp))
            {
                var reason = assimp.GetErrorStringS();
                throw new ExportException(UiText.ExportAssimpFailed(string.IsNullOrWhiteSpace(reason) ? ret.ToString() : reason));
            }

            if (fmt.Key == "fbx")
            {
                FbxUnits.PatchToMetres(tmp);
            }

            FixSidecars(tmp, path, fmt, extra);
            File.Move(tmp, path, overwrite: true);
        }
        catch (ExportException)
        {
            TryDelete(tmp);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(tmp);
            throw new ExportException(UiText.ExportAssimpFailed(ex.Message), ex);
        }
        finally
        {
            foreach (var p in natives)
            {
                NativeMemory.Free((void*)p);
            }
        }

        return new ExportResult
        {
            Path = path,
            Format = fmt.Key,
            ObjectCount = list.Count,
            MeshCount = b.Meshes.Count,
            VertexCount = b.Vertices,
            TriangleCount = b.Triangles,
            Min = b.Min,
            Max = b.Max,
            ExtraFiles = extra,
        };
    }

    /// <summary>OBJ (.mtl) and glTF (.bin) side files are named after the temp file; rename them and fix the reference.</summary>
    static void FixSidecars(string tmp, string path, ExportFormat fmt, List<string> extra)
    {
        string? side = fmt.Key switch { "obj" => ".mtl", "gltf" => ".bin", _ => null };
        if (side == null)
        {
            return;
        }

        var tmpName = Path.GetFileNameWithoutExtension(tmp);
        var folder = Path.GetDirectoryName(tmp)!;
        var tmpSide = Path.Combine(folder, tmpName + side);
        if (!File.Exists(tmpSide))
        {
            return;
        }

        var finalSide = Path.ChangeExtension(path, side);
        File.Move(tmpSide, finalSide, overwrite: true);
        extra.Add(finalSide);
        var text = File.ReadAllText(tmp);
        text = text.Replace(tmpName + side, Path.GetFileName(finalSide));
        File.WriteAllText(tmp, text, new UTF8Encoding(false));
    }

    static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch
        {
            // best effort
        }
    }

    static string UniqueName(Builder b, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Object";
        }

        var n = name;
        for (int i = 1; !b.NodeNames.Add(n); i++)
        {
            n = $"{name}.{i:000}";
        }

        return n;
    }

    static void AddObject(Builder b, XNode root, SceneObject o, bool flat, Matrix4x4 axis, bool textures, ExportOptions options)
    {
        var mesh = o.Mesh;
        var nodes = mesh.Nodes;
        var parts = mesh.Parts.Length > 0
            ? mesh.Parts
            : [new MeshPart { StartIndex = 0, IndexCount = mesh.Indices.Length }];
        var offset = Matrix4x4.CreateTranslation(o.ImportOffset);
        var objectNode = new XNode
        {
            Name = UniqueName(b, Path.GetFileNameWithoutExtension(o.Name)),
            Local = flat ? Matrix4x4.Identity : Matrix4x4.CreateTranslation(-o.ImportOffset) * o.World,
        };
        root.Children.Add(objectNode);

        // Hidden nodes hide their whole subtree (same as the viewport).
        var hidden = new bool[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            hidden[i] = options.SkipHidden && (o.HiddenNodes.Contains(i) || (nodes[i].Parent >= 0 && hidden[nodes[i].Parent]));
        }

        var used = new bool[parts.Length];
        var xnodes = new XNode[nodes.Length];
        for (int i = 0; i < nodes.Length; i++)
        {
            var n = nodes[i];
            var parent = n.Parent >= 0 && n.Parent < i ? xnodes[n.Parent] : objectNode;
            var target = parent;
            Matrix4x4 vertexMatrix;
            if (flat)
            {
                vertexMatrix = o.World * axis;
            }
            else
            {
                var x = new XNode { Name = UniqueName(b, n.Name), Local = n.Local };
                parent.Children.Add(x);
                xnodes[i] = x;
                target = x;
                if (Matrix4x4.Invert(n.World, out var inv) && MathF.Abs(n.World.GetDeterminant()) > 1e-12f)
                {
                    vertexMatrix = offset * inv;
                }
                else
                {
                    // Degenerate node matrix: keep the geometry on the object node instead.
                    target = objectNode;
                    vertexMatrix = offset;
                }
            }

            xnodes[i] ??= objectNode;
            foreach (int p in n.PartIndices)
            {
                if (p < 0 || p >= parts.Length || used[p])
                {
                    continue;
                }

                used[p] = true;
                if (!hidden[i])
                {
                    AddPart(b, target, o, parts[p], $"{n.Name}", vertexMatrix, flat, axis, textures, options);
                }
            }
        }

        // Parts no node claims (or a mesh without a Parts table) hang off the object node.
        for (int p = 0; p < parts.Length; p++)
        {
            if (!used[p])
            {
                AddPart(b, objectNode, o, parts[p], objectNode.Name, flat ? o.World * axis : offset, flat, axis, textures, options);
            }
        }
    }

    static void AddPart(Builder b, XNode target, SceneObject o, MeshPart part, string name, Matrix4x4 vertexMatrix,
        bool flat, Matrix4x4 axis, bool textures, ExportOptions options)
    {
        var mesh = o.Mesh;
        int start = Math.Max(0, part.StartIndex);
        int end = Math.Min(mesh.Indices.Length, part.StartIndex + part.IndexCount);
        end -= (end - start) % 3;
        if (end <= start)
        {
            return;
        }

        Matrix4x4.Invert(vertexMatrix, out var inverse);
        var normalMatrix = Matrix4x4.Transpose(inverse);
        bool flip = vertexMatrix.GetDeterminant() < 0;
        var world = o.World;

        var remap = new Dictionary<uint, uint>();
        var source = new List<uint>();
        var indices = new uint[end - start];
        for (int i = start; i < end; i += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                uint vi = mesh.Indices[i + (flip && k > 0 ? 3 - k : k)];
                if (!remap.TryGetValue(vi, out var ni))
                {
                    ni = (uint)source.Count;
                    remap[vi] = ni;
                    source.Add(vi);
                }

                indices[i - start + k] = ni;
            }
        }

        var positions = new Vector3[source.Count];
        var normals = new Vector3[source.Count];
        var uvs = new Vector3[source.Count];
        var colors = new Vector4[source.Count];
        bool uniform = true;
        for (int i = 0; i < source.Count; i++)
        {
            var v = mesh.Vertices[source[i]];
            positions[i] = Vector3.Transform(v.Position, vertexMatrix);
            var nrm = Vector3.TransformNormal(v.Normal, normalMatrix);
            normals[i] = nrm.LengthSquared() > 1e-20f ? Vector3.Normalize(nrm) : Vector3.UnitY;
            // The importer flips V (top-left origin); Assimp scenes use bottom-left.
            uvs[i] = new Vector3(v.TexCoord.X, 1f - v.TexCoord.Y, 0);
            colors[i] = Unpack(v.Color);
            uniform &= colors[i] == colors[0];

            var p = Vector3.Transform(v.Position, world);
            b.Min = Vector3.Min(b.Min, p);
            b.Max = Vector3.Max(b.Max, p);
        }

        int texture = -1;
        if (textures && part.TextureIndex >= 0 && part.TextureIndex < mesh.Textures.Length)
        {
            texture = TextureSlot(b, o, part.TextureIndex);
        }

        bool vertexColors = !uniform && options.IncludeVertexColors && !flat;
        var color = uniform ? colors[0] : Vector4.One;
        var key = (color, texture, part.DoubleSided);
        if (!b.MaterialLookup.TryGetValue(key, out int material))
        {
            material = b.Materials.Count;
            b.Materials.Add(new XMaterial($"Material.{material:000}", color, texture, part.DoubleSided));
            b.MaterialLookup[key] = material;
        }

        target.Meshes.Add(b.Meshes.Count);
        b.Meshes.Add(new XMesh
        {
            Name = name,
            Positions = positions,
            Normals = normals,
            TexCoords = flat ? null : uvs,
            Colors = vertexColors ? colors : null,
            Indices = indices,
            Material = material,
        });
        b.Vertices += positions.Length;
        b.Triangles += indices.Length / 3;
    }

    static Vector4 Unpack(uint c) => new Vector4(c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF, (c >> 24) & 0xFF) / 255f;

    static int TextureSlot(Builder b, SceneObject o, int index)
    {
        if (b.TextureLookup.TryGetValue((o.Id, index), out int slot))
        {
            return slot;
        }

        var tex = o.Mesh.Textures[index];
        var bitmap = BitmapSource.Create(tex.Width, tex.Height, 96, 96, PixelFormats.Bgra32, null, tex.Bgra, tex.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        slot = b.Textures.Count;
        b.Textures.Add((ms.ToArray(), $"{SafeFileName(Path.GetFileNameWithoutExtension(o.Name))}_tex{slot}.png"));
        b.TextureLookup[(o.Id, index)] = slot;
        return slot;
    }

    static string SafeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "texture" : name;
    }

    // ---------------------------------------------------------------- native aiScene

    static T* Alloc<T>(List<nint> natives, int count = 1) where T : unmanaged
    {
        var p = NativeMemory.AllocZeroed((nuint)Math.Max(1, count), (nuint)sizeof(T));
        natives.Add((nint)p);
        return (T*)p;
    }

    static T* Copy<T>(List<nint> natives, T[] data) where T : unmanaged
    {
        var p = Alloc<T>(natives, data.Length);
        data.AsSpan().CopyTo(new Span<T>(p, data.Length));
        return p;
    }

    static AiScene* BuildScene(Builder b, XNode root, bool embedTextures, List<nint> natives)
    {
        var scene = Alloc<AiScene>(natives);
        scene->MName = new AssimpString("PolyView");

        scene->MNumMeshes = (uint)b.Meshes.Count;
        scene->MMeshes = (AiMesh**)Alloc<nint>(natives, b.Meshes.Count);
        for (int i = 0; i < b.Meshes.Count; i++)
        {
            scene->MMeshes[i] = BuildMesh(b.Meshes[i], natives);
        }

        scene->MNumMaterials = (uint)b.Materials.Count;
        scene->MMaterials = (AiMaterial**)Alloc<nint>(natives, b.Materials.Count);
        for (int i = 0; i < b.Materials.Count; i++)
        {
            var m = b.Materials[i];
            string? texPath = m.Texture < 0 ? null : embedTextures ? $"*{m.Texture}" : b.Textures[m.Texture].FileName;
            scene->MMaterials[i] = BuildMaterial(m, texPath, natives);
        }

        if (embedTextures && b.Textures.Count > 0)
        {
            scene->MNumTextures = (uint)b.Textures.Count;
            scene->MTextures = (AiTexture**)Alloc<nint>(natives, b.Textures.Count);
            for (int i = 0; i < b.Textures.Count; i++)
            {
                var t = Alloc<AiTexture>(natives);
                var png = b.Textures[i].Png;
                t->MWidth = (uint)png.Length;
                t->MHeight = 0;
                t->AchFormatHint[0] = (byte)'p';
                t->AchFormatHint[1] = (byte)'n';
                t->AchFormatHint[2] = (byte)'g';
                t->PcData = (Texel*)Copy(natives, png);
                t->MFilename = new AssimpString(b.Textures[i].FileName);
                scene->MTextures[i] = t;
            }
        }

        scene->MRootNode = BuildNode(root, null, natives);
        return scene;
    }

    static AiMesh* BuildMesh(XMesh x, List<nint> natives)
    {
        var m = Alloc<AiMesh>(natives);
        m->MName = new AssimpString(x.Name);
        m->MPrimitiveTypes = (uint)PrimitiveType.Triangle;
        m->MNumVertices = (uint)x.Positions.Length;
        m->MVertices = Copy(natives, x.Positions);
        m->MNormals = Copy(natives, x.Normals);
        if (x.TexCoords != null)
        {
            m->MTextureCoords[0] = Copy(natives, x.TexCoords);
            m->MNumUVComponents[0] = 2;
        }

        if (x.Colors != null)
        {
            m->MColors[0] = Copy(natives, x.Colors);
        }

        int faces = x.Indices.Length / 3;
        m->MNumFaces = (uint)faces;
        m->MFaces = Alloc<Face>(natives, faces);
        var idx = Copy(natives, x.Indices);
        for (int f = 0; f < faces; f++)
        {
            m->MFaces[f].MNumIndices = 3;
            m->MFaces[f].MIndices = idx + f * 3;
        }

        m->MMaterialIndex = (uint)x.Material;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in x.Positions)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        m->MAABB = new Silk.NET.Maths.Box3D<float>(new Silk.NET.Maths.Vector3D<float>(min.X, min.Y, min.Z), new Silk.NET.Maths.Vector3D<float>(max.X, max.Y, max.Z));
        return m;
    }

    static AiNode* BuildNode(XNode x, AiNode* parent, List<nint> natives)
    {
        var n = Alloc<AiNode>(natives);
        n->MName = new AssimpString(x.Name);
        // System.Numerics is row-vector; Assimp is column-vector.
        n->MTransformation = Matrix4x4.Transpose(x.Local);
        n->MParent = parent;
        if (x.Meshes.Count > 0)
        {
            n->MNumMeshes = (uint)x.Meshes.Count;
            n->MMeshes = Copy(natives, x.Meshes.Select(i => (uint)i).ToArray());
        }

        if (x.Children.Count > 0)
        {
            n->MNumChildren = (uint)x.Children.Count;
            n->MChildren = (AiNode**)Alloc<nint>(natives, x.Children.Count);
            for (int i = 0; i < x.Children.Count; i++)
            {
                n->MChildren[i] = BuildNode(x.Children[i], n, natives);
            }
        }

        return n;
    }

    static AiMaterial* BuildMaterial(XMaterial m, string? texture, List<nint> natives)
    {
        var props = new List<nint>();
        var color = m.Color;
        props.Add((nint)StringProp("?mat.name", 0, m.Name, natives));
        props.Add((nint)FloatProp("$clr.diffuse", [color.X, color.Y, color.Z, color.W], natives));
        props.Add((nint)FloatProp("$clr.base", [color.X, color.Y, color.Z, color.W], natives));
        props.Add((nint)FloatProp("$mat.opacity", [color.W], natives));
        props.Add((nint)FloatProp("$mat.metallicFactor", [0f], natives));
        props.Add((nint)FloatProp("$mat.roughnessFactor", [0.5f], natives));
        props.Add((nint)IntProp("$mat.twosided", m.DoubleSided ? 1 : 0, natives));
        if (texture != null)
        {
            props.Add((nint)StringProp("$tex.file", (uint)TextureType.Diffuse, texture, natives));
            props.Add((nint)StringProp("$tex.file", (uint)TextureType.BaseColor, texture, natives));
        }

        var mat = Alloc<AiMaterial>(natives);
        mat->MNumProperties = (uint)props.Count;
        mat->MNumAllocated = (uint)props.Count;
        mat->MProperties = (MaterialProperty**)Copy(natives, props.ToArray());
        return mat;
    }

    static MaterialProperty* NewProp(string key, uint semantic, PropertyTypeInfo type, byte[] data, List<nint> natives)
    {
        var p = Alloc<MaterialProperty>(natives);
        p->MKey = new AssimpString(key);
        p->MSemantic = semantic;
        p->MIndex = 0;
        p->MType = type;
        p->MDataLength = (uint)data.Length;
        p->MData = Copy(natives, data);
        return p;
    }

    static MaterialProperty* FloatProp(string key, float[] values, List<nint> natives) =>
        NewProp(key, 0, PropertyTypeInfo.Float, MemoryMarshal.AsBytes(values.AsSpan()).ToArray(), natives);

    static MaterialProperty* IntProp(string key, int value, List<nint> natives) =>
        NewProp(key, 0, PropertyTypeInfo.Integer, BitConverter.GetBytes(value), natives);

    /// <summary>aiString layout: uint32 length, UTF-8 bytes, terminating zero.</summary>
    static MaterialProperty* StringProp(string key, uint semantic, string value, List<nint> natives)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var data = new byte[4 + bytes.Length + 1];
        BitConverter.GetBytes((uint)bytes.Length).CopyTo(data, 0);
        bytes.CopyTo(data, 4);
        return NewProp(key, semantic, PropertyTypeInfo.String, data, natives);
    }
}
