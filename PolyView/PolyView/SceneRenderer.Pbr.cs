using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace PolyView;

/// <summary>
/// Material system renderer side (docs/13 part 2 §3): Cook-Torrance GGX + Smith-GGX + Schlick for Material Preview /
/// Rendered, material constants in cbuffer b2, textures t0-t4 with 1x1 defaults, and Opaque / Mask / Blend passes
/// (Blend sorted back-to-front by object centre, depth write off). Solid mode keeps the Blender studio look.
/// </summary>
public sealed partial class SceneRenderer
{
    // Prepended to ShaderSource (after ShadingSource).
    const string PbrSource = """
        static const float PI = 3.14159265;
        static const float3 PreviewSky = float3(0.42, 0.44, 0.48);
        static const float3 PreviewGround = float3(0.16, 0.15, 0.14);
        static const float PreviewKey = 2.4;
        static const float PreviewFill = 0.7;

        // Formulas follow Google Filament's material documentation (Apache-2.0),
        // https://google.github.io/filament/Filament.html section 4 "Material system",
        // cross-checked against KhronosGroup/glTF-Sample-Viewer (Apache-2.0) brdf.glsl.
        struct PbrSurface
        {
            float3 N;
            float3 V;
            float3 Albedo;
            float Metallic;
            float Roughness;   // perceptual roughness; alpha = roughness^2
            float Ior;
        };

        // Filament 4.4.1 / glTF-Sample-Viewer F_Schlick.
        float3 FresnelSchlick(float voh, float3 f0)
        {
            float f = pow(1 - voh, 5);
            return f + f0 * (1 - f);
        }

        // Filament 4.4.1 D_GGX (Trowbridge-Reitz), glTF-Sample-Viewer D_GGX.
        float DistributionGGX(float noh, float a)
        {
            float a2 = a * a;
            float f = (noh * a2 - noh) * noh + 1;
            return a2 / (PI * f * f + 1e-7);
        }

        // Filament 4.4.2 V_SmithGGXCorrelated (height-correlated Smith), glTF-Sample-Viewer V_GGX.
        float VisibilitySmithCorrelated(float nov, float nol, float a)
        {
            float a2 = a * a;
            float ggxl = nov * sqrt(nol * nol * (1 - a2) + a2);
            float ggxv = nol * sqrt(nov * nov * (1 - a2) + a2);
            return 0.5 / max(ggxv + ggxl, 1e-5);
        }

        // Dielectric F0 from IOR (Filament 4.8.3.2 / glTF KHR_materials_ior): ((ior - 1) / (ior + 1))^2; 1.5 -> 0.04.
        float3 SurfaceF0(PbrSurface s)
        {
            float r = (s.Ior - 1) / (s.Ior + 1);
            return lerp((r * r).xxx, s.Albedo, s.Metallic);
        }

        // Radiance is in the viewport's units where a Lambert surface reflects albedo * radiance * N.L
        // (the 1/pi of Lambert is folded into the light scale), so the specular lobe is multiplied by pi to match.
        // Diffuse: Lambert (Filament 4.5.1), scaled by (1 - F) so diffuse + specular never exceeds the input energy.
        // Not yet: multiple-scattering energy compensation (Filament 4.7.2, needs the DFG LUT; stage 2 with IBL).
        float3 PbrDirect(PbrSurface s, float3 L, float3 radiance)
        {
            float nol = saturate(dot(s.N, L));
            if (nol <= 0)
            {
                return 0;
            }

            float3 H = normalize(L + s.V);
            float nov = abs(dot(s.N, s.V)) + 1e-5;
            float noh = saturate(dot(s.N, H));
            float voh = saturate(dot(s.V, H));
            float a = s.Roughness * s.Roughness;
            float3 F = FresnelSchlick(voh, SurfaceF0(s));
            float3 spec = DistributionGGX(noh, a) * VisibilitySmithCorrelated(nov, nol, a) * F;
            float3 diffuse = (1 - F) * (1 - s.Metallic) * s.Albedo;
            return (diffuse + spec * PI) * radiance * nol;
        }

        // Simple hemisphere ambient (P0; P1 replaces it with HDRI / IBL).
        float3 PbrAmbient(PbrSurface s, float3 sky, float3 ground)
        {
            float3 env = lerp(ground, sky, s.N.y * 0.5 + 0.5);
            float3 R = reflect(-s.V, s.N);
            float3 envSpec = lerp(env, lerp(ground, sky, R.y * 0.5 + 0.5), 1 - s.Roughness);
            float ndv = saturate(dot(s.N, s.V));
            float3 f0 = SurfaceF0(s);
            float3 F = f0 + (max((1 - s.Roughness).xxx, f0) - f0) * pow(1 - ndv, 5);
            return (1 - F) * (1 - s.Metallic) * s.Albedo * env + F * envSpec;
        }

        """;

    /// <summary>cbuffer MaterialCB (b2), 64 bytes.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialParams
    {
        public Vector4 BaseColor;
        public Vector3 Emissive;
        public float Metallic;
        public float Roughness;
        public float NormalScale;
        public float AlphaCutoff;
        public float OcclusionStrength;
        public uint Flags;
        public float Ior;
        public Vector2 Pad;

        public const uint FlagHasMaterial = 4;
        public const uint FlagDoubleSided = 8;

        public readonly AlphaMode AlphaMode => (AlphaMode)(Flags & 3);
        public readonly bool DoubleSided => (Flags & FlagDoubleSided) != 0;

        /// <summary>Empty slot: vertex colour, default grey look, roughness 0.5, opaque.</summary>
        public static MaterialParams Empty => new() { BaseColor = Vector4.One, Roughness = 0.5f, NormalScale = 1, AlphaCutoff = 0.5f, OcclusionStrength = 1, Ior = 1.5f };

        public static MaterialParams From(PvMaterial m) => new()
        {
            BaseColor = m.BaseColor,
            Emissive = m.EmissiveColor * m.EmissiveStrength,
            Metallic = m.Metallic,
            Roughness = m.Roughness,
            NormalScale = m.NormalScale,
            AlphaCutoff = m.AlphaCutoff,
            OcclusionStrength = m.OcclusionStrength,
            Ior = m.Ior,
            Flags = (uint)m.AlphaMode | FlagHasMaterial | (m.DoubleSided ? FlagDoubleSided : 0),
        };
    }

    ID3D11Buffer _materialConstants = null!;
    readonly ID3D11Buffer[] _materialSlot = new ID3D11Buffer[1];
    ID3D11Texture2D _flatNormalTexture = null!;
    ID3D11ShaderResourceView _flatNormalView = null!;
    readonly ID3D11ShaderResourceView[] _defaultMaps = new ID3D11ShaderResourceView[4];
    ID3D11BlendState _transparentBlend = null!;
    readonly List<(float Distance, int Index)> _blendOrder = [];
    MaterialParams _writtenMaterial;
    bool _materialWritten;

    void InitMaterials()
    {
        _materialConstants = _device.CreateBuffer((uint)Marshal.SizeOf<MaterialParams>(), BindFlags.ConstantBuffer, ResourceUsage.Dynamic, CpuAccessFlags.Write);
        (_flatNormalTexture, _flatNormalView) = CreateTexture(1, 1, [255, 128, 128, 255]); // BGRA of (0.5, 0.5, 1)
        // t1 metallic/roughness white (factor only), t2 flat normal, t3 occlusion white, t4 emissive white.
        _defaultMaps[0] = _whiteView;
        _defaultMaps[1] = _flatNormalView;
        _defaultMaps[2] = _whiteView;
        _defaultMaps[3] = _whiteView;
        _transparentBlend = _device.CreateBlendState(BlendDescription.NonPremultiplied);
    }

    void DisposeMaterials()
    {
        _materialConstants.Dispose();
        _flatNormalView.Dispose();
        _flatNormalTexture.Dispose();
        _transparentBlend.Dispose();
    }

    /// <summary>Material values of every slot of an object, and which slot each mesh part uses (null = slot 0).</summary>
    public void SetMaterials(int id, MaterialParams[] slots, int[]? partSlots)
    {
        for (int i = 0; i < _objects.Count; i++)
        {
            if (_objects[i].Id == id)
            {
                _objects[i].Materials = slots;
                _objects[i].PartSlots = partSlots;
            }
        }
    }

    static MaterialParams PartMaterial(GpuObject gpu, int part)
    {
        int slot = gpu.PartSlots != null && part < gpu.PartSlots.Length ? gpu.PartSlots[part] : 0;
        return slot >= 0 && slot < gpu.Materials.Length ? gpu.Materials[slot] : MaterialParams.Empty;
    }

    unsafe void WriteMaterial(in MaterialParams m)
    {
        if (_materialWritten && _writtenMaterial.Equals(m))
        {
            return;
        }

        var mapped = _context.Map(_materialConstants, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        *(MaterialParams*)mapped.DataPointer = m;
        _context.Unmap(_materialConstants, 0);
        _writtenMaterial = m;
        _materialWritten = true;
    }

    bool PartDrawable(GpuObject gpu, int p)
    {
        var part = gpu.Parts[p];
        return part.IndexCount > 0 && part.StartIndex + part.IndexCount <= gpu.IndexCount
            && !(gpu.PartHidden != null && p < gpu.PartHidden.Length && gpu.PartHidden[p]);
    }

    /// <summary>Pass 1 opaque + mask, pass 2 blend (back-to-front by object centre, no depth write).</summary>
    void DrawMeshes(in Matrix4x4 sceneWorld, in Matrix4x4 viewProjection, Vector3 light)
    {
        _materialWritten = false;
        _materialSlot[0] = _materialConstants;
        _context.PSSetConstantBuffers(2, 1, _materialSlot);
        _context.PSSetShaderResources(1, 4, _defaultMaps);
        _blendOrder.Clear();
        for (int o = 0; o < _objects.Count; o++)
        {
            var gpu = _objects[o];
            if (gpu.IndexCount <= 0 || !gpu.Visible)
            {
                continue;
            }

            if (DrawObject(gpu, sceneWorld, viewProjection, light, blendPass: false))
            {
                var centre = Vector3.Transform(Vector3.Zero, gpu.World * sceneWorld);
                _blendOrder.Add((Vector3.DistanceSquared(centre, _eye), o));
            }
        }

        if (_blendOrder.Count == 0)
        {
            return;
        }

        _blendOrder.Sort(static (a, b) => b.Distance.CompareTo(a.Distance));
        _context.OMSetDepthStencilState(_gridDepthState, 0);
        _context.OMSetBlendState(_transparentBlend);
        foreach (var (_, index) in _blendOrder)
        {
            DrawObject(_objects[index], sceneWorld, viewProjection, light, blendPass: true);
        }

        _context.OMSetBlendState(null);
        _context.OMSetDepthStencilState(_depthState, 0);
    }

    /// <summary>Draws the parts of one pass; in the opaque pass returns true when the object has blend parts.</summary>
    bool DrawObject(GpuObject gpu, in Matrix4x4 sceneWorld, in Matrix4x4 viewProjection, Vector3 light, bool blendPass)
    {
        bool hasBlend = false;
        bool bound = false;
        var objectWorld = gpu.World * sceneWorld;
        var parts = gpu.Parts;
        for (int p = 0; p < parts.Length; p++)
        {
            if (!PartDrawable(gpu, p))
            {
                continue;
            }

            var mat = PartMaterial(gpu, p);
            bool blend = mat.AlphaMode == AlphaMode.Blend;
            if (blend != blendPass)
            {
                hasBlend |= blend;
                continue;
            }

            if (!bound)
            {
                WriteConstants(objectWorld, viewProjection, light, unlit: false, IsSelected(gpu.Id) ? 1f : 0f);
                _vertexSlot[0] = gpu.Vertices;
                _context.IASetVertexBuffers(0, 1, _vertexSlot, _stride, _offset);
                _context.IASetIndexBuffer(gpu.Indices, Format.R32_UInt, 0);
                bound = true;
            }

            var part = parts[p];
            WriteMaterial(mat);
            bool doubleSided = part.DoubleSided || mat.DoubleSided;
            _context.RSSetState(doubleSided
                ? (Wireframe ? _doubleSidedWireRasterizer : _doubleSidedRasterizer)
                : (Wireframe ? _wireRasterizer : _rasterizer));
            _textureSlot[0] = part.TextureIndex >= 0 && part.TextureIndex < gpu.Textures.Count
                ? gpu.Textures[part.TextureIndex].View
                : _whiteView;
            _context.PSSetShaderResources(0, 1, _textureSlot);
            _context.DrawIndexed((uint)part.IndexCount, (uint)part.StartIndex, 0);
        }

        return hasBlend;
    }
}
