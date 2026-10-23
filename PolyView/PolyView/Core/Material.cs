using System.Numerics;

namespace PolyView;

/// <summary>Blender 4.2 style alpha handling (glTF alphaMode): Opaque, Mask (alpha clip at AlphaCutoff), Blend (sorted back-to-front).</summary>
public enum AlphaMode
{
    Opaque,
    Mask,
    Blend,
}

/// <summary>Reference to a texture in the scene's texture cache (P1: imported / user textures).</summary>
public sealed class TextureRef
{
    public string Key { get; set; } = "";
    public int TexCoord { get; set; }
}

/// <summary>Material data-block (docs/13 part 2 §1). Can be shared by several objects; base color is linear.</summary>
/// <remarks>Named PvMaterial to avoid clashing with Silk.NET.Assimp.Material and SharpGLTF.Schema2.Material inside namespace PolyView.</remarks>
public sealed class PvMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Material";
    public Vector4 BaseColor { get; set; } = new(0.8f, 0.8f, 0.8f, 1f);
    public TextureRef? BaseColorTexture { get; set; }
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 0.5f;
    /// <summary>Index of refraction (Principled BSDF IOR, 1.0-4.0, default 1.5 = F0 0.04).</summary>
    public float Ior { get; set; } = 1.5f;
    public TextureRef? MetallicRoughnessTexture { get; set; }
    public TextureRef? NormalTexture { get; set; }
    public float NormalScale { get; set; } = 1f;
    public TextureRef? OcclusionTexture { get; set; }
    public float OcclusionStrength { get; set; } = 1f;
    public Vector3 EmissiveColor { get; set; } = Vector3.One;
    /// <summary>Blender Emission Strength (default 0 = no emission).</summary>
    public float EmissiveStrength { get; set; }
    public TextureRef? EmissiveTexture { get; set; }
    public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;
    public float AlphaCutoff { get; set; } = 0.5f;
    /// <summary>True = Blender Backface Culling off.</summary>
    public bool DoubleSided { get; set; }
    public bool FakeUser { get; set; }
    /// <summary>Bumped on every change so GPU caches can rebuild.</summary>
    public int Version { get; set; }

    /// <summary>Copy every editable value (not Id / Version) from another material.</summary>
    public void CopyFrom(PvMaterial s)
    {
        Name = s.Name; BaseColor = s.BaseColor; BaseColorTexture = s.BaseColorTexture; Metallic = s.Metallic; Roughness = s.Roughness; Ior = s.Ior;
        MetallicRoughnessTexture = s.MetallicRoughnessTexture; NormalTexture = s.NormalTexture; NormalScale = s.NormalScale;
        OcclusionTexture = s.OcclusionTexture; OcclusionStrength = s.OcclusionStrength; EmissiveColor = s.EmissiveColor;
        EmissiveStrength = s.EmissiveStrength; EmissiveTexture = s.EmissiveTexture; AlphaMode = s.AlphaMode; AlphaCutoff = s.AlphaCutoff;
        DoubleSided = s.DoubleSided; FakeUser = s.FakeUser;
        Version++;
    }

    public PvMaterial CloneValues()
    {
        var m = new PvMaterial { Id = Id };
        m.CopyFrom(this);
        return m;
    }
}

/// <summary>Socket between an object and a material (Link = Object in P0). Material null = default grey.</summary>
public sealed class MaterialSlot
{
    public PvMaterial? Material { get; set; }
}

/// <summary>An index range of the mesh drawn with one material slot (glTF primitive / Assimp mesh).</summary>
public sealed class SubMesh
{
    public int IndexStart { get; init; }
    public int IndexCount { get; init; }
    public int MaterialIndex { get; init; }
}

/// <summary>All material data-blocks of the scene.</summary>
public sealed class MaterialLibrary
{
    public List<PvMaterial> Materials { get; } = [];

    /// <summary>Used by empty slots: Blender's default grey (0.8, roughness 0.5). Never listed, never saved.</summary>
    public PvMaterial Default { get; } = new() { Name = "Default" };

    public PvMaterial? Find(Guid id) => Materials.Find(m => m.Id == id);

    public string UniqueName(string baseName)
    {
        if (!Materials.Exists(m => m.Name == baseName)) return baseName;
        for (int i = 1; ; i++)
        {
            var n = $"{baseName}.{i:000}";
            if (!Materials.Exists(m => m.Name == n)) return n;
        }
    }
}
