using System.Numerics;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace PolyView;

/// <summary>
/// Blender 4.2 Solid viewport look: default 0.8 linear grey, two-light studio with soft wrap,
/// low specular, lighting in linear space with a single sRGB encode (the back buffer is UNorm,
/// not _SRGB, so the shader encodes exactly once), flat #3D3D3D -> #303030 background, #545454 grid.
/// </summary>
public sealed partial class SceneRenderer
{
    /// <summary>Grid line colours (display / sRGB values, drawn unlit).</summary>
    static readonly Vector4 GridMinorColor = new(0x54 / 255f, 0x54 / 255f, 0x54 / 255f, 1f);
    static readonly Vector4 GridMajorColor = new(0x5E / 255f, 0x5E / 255f, 0x5E / 255f, 1f);

    // Prepended to ShaderSource, so PSMain can call these.
    const string ShadingSource = """
        static const float3 BackgroundTop = float3(0.23922, 0.23922, 0.23922);    // #3D3D3D
        static const float3 BackgroundBottom = float3(0.18824, 0.18824, 0.18824); // #303030
        static const float SolidDefaultAlbedo = 0.8;   // Blender default object colour (linear)
        static const float StudioKeyUp = 0.6;          // tilt the camera-following key towards the zenith
        static const float StudioWrap = 0.3;           // soft wrap of both studio lights
        static const float StudioKey = 0.27;
        static const float StudioFill = 0.07;
        static const float StudioSky = 0.13;
        static const float StudioGround = 0.11;
        static const float StudioSpecular = 0.035;
        static const float StudioShininess = 24;

        float3 SrgbToLinear(float3 c)
        {
            return c <= 0.04045 ? c / 12.92 : pow(abs((c + 0.055) / 1.055), 2.4);
        }

        float3 LinearToSrgb(float3 c)
        {
            c = saturate(c);
            return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055;
        }

        // Linear albedo in (vertex / material colours are linear; pure white = "no colour" = 0.8 grey),
        // sRGB display colour out.
        float3 StudioSolid(float3 n, float3 v, float3 keyDir, float3 albedo)
        {
            if (all(albedo > 0.999))
            {
                albedo = SolidDefaultAlbedo.xxx;
            }

            keyDir = normalize(lerp(keyDir, float3(0, 1, 0), StudioKeyUp));
            float3 fillDir = normalize(float3(-keyDir.x, 0.25, -keyDir.z) + 1e-5);
            float key = saturate((dot(n, keyDir) + StudioWrap) / (1 + StudioWrap));
            float fill = saturate((dot(n, fillDir) + StudioWrap) / (1 + StudioWrap));
            float hemi = lerp(StudioGround, StudioSky, n.y * 0.5 + 0.5);
            float spec = pow(saturate(dot(n, normalize(keyDir + v))), StudioShininess) * StudioSpecular;
            float3 lin = albedo * (hemi + StudioKey * key + StudioFill * fill) + spec;
            // Soft shoulder above 0.7 so bright colours never blow out to white.
            float3 over = max(lin - 0.7, 0);
            lin = min(lin, 0.7) + over / (1 + over / 0.3);
            return LinearToSrgb(lin);
        }

        struct BgOut
        {
            float4 Position : SV_Position;
            float V : TEXCOORD0;
        };

        BgOut VSBackground(uint id : SV_VertexID)
        {
            BgOut o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.Position = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            o.V = uv.y;
            return o;
        }

        float4 PSBackground(BgOut input) : SV_Target
        {
            return float4(lerp(BackgroundTop, BackgroundBottom, saturate(input.V)), 1);
        }

        """;

    ID3D11VertexShader? _backgroundVertexShader;
    ID3D11PixelShader? _backgroundPixelShader;

    void DrawBackground()
    {
        _backgroundVertexShader ??= _device.CreateVertexShader(Compile("VSBackground", "vs_5_0"));
        _backgroundPixelShader ??= _device.CreatePixelShader(Compile("PSBackground", "ps_5_0"));
        _context.OMSetDepthStencilState(_noDepthState, 0);
        _context.RSSetState(_doubleSidedRasterizer);
        _context.OMSetBlendState(null);
        _context.IASetInputLayout(null);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_backgroundVertexShader);
        _context.PSSetShader(_backgroundPixelShader);
        _context.Draw(3, 0);
    }
}
