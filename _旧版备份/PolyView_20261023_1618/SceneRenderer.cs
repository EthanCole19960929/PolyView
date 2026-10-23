using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace PolyView;

public sealed class SceneRenderer : IDisposable
{
    const string ShaderSource = """
        cbuffer Frame : register(b0)
        {
            float4x4 WorldViewProj;
            float4x4 NormalWorld;
            float3 LightDirection;
        };

        struct VsIn
        {
            float3 Position : POSITION;
            float3 Normal : NORMAL;
        };

        struct VsOut
        {
            float4 Position : SV_Position;
            float3 Normal : TEXCOORD0;
        };

        VsOut VSMain(VsIn input)
        {
            VsOut output;
            output.Position = mul(WorldViewProj, float4(input.Position, 1));
            output.Normal = mul((float3x3)NormalWorld, input.Normal);
            return output;
        }

        float4 PSMain(VsOut input) : SV_Target
        {
            float3 normal = normalize(input.Normal);
            float light = saturate(dot(normal, LightDirection));
            float3 color = float3(0.73, 0.77, 0.82) * (0.22 + 0.78 * light);
            return float4(color, 1);
        }
        """;

    readonly ID3D11Device _device;
    readonly ID3D11DeviceContext _context;
    readonly ID3D11VertexShader _vertexShader;
    readonly ID3D11PixelShader _pixelShader;
    readonly ID3D11InputLayout _layout;
    readonly ID3D11Buffer _constants;
    readonly ID3D11DepthStencilState _depthState;
    readonly ID3D11RasterizerState _rasterizer;
    readonly ID3D11RasterizerState _wireRasterizer;
    ID3D11Texture2D? _color;
    ID3D11RenderTargetView? _colorView;
    ID3D11Texture2D? _depth;
    ID3D11DepthStencilView? _depthView;
    readonly ID3D11RenderTargetView[] _targets = new ID3D11RenderTargetView[1];
    readonly Viewport[] _viewports = new Viewport[1];
    readonly ID3D11Buffer[] _constantSlot = new ID3D11Buffer[1];
    readonly ID3D11Buffer[] _vertexSlot = new ID3D11Buffer[1];
    readonly uint[] _stride = new uint[1];
    readonly uint[] _offset = new uint[1];
    ID3D11Buffer? _vertices;
    ID3D11Buffer? _indices;
    int _indexCount;
    int _pixelWidth = 1;
    int _pixelHeight = 1;

    public ID3D11Texture2D? ColorTexture => _color;
    public string DeviceName { get; }
    public bool Wireframe { get; set; }

    SceneRenderer(ID3D11Device device, ID3D11DeviceContext context, string deviceName)
    {
        _device = device;
        _context = context;
        DeviceName = deviceName;
        using var multithread = device.QueryInterface<ID3D11Multithread>();
        multithread.SetMultithreadProtected(true);

        var bytecode = Compile("VSMain", "vs_5_0");
        _vertexShader = device.CreateVertexShader(bytecode);
        _pixelShader = device.CreatePixelShader(Compile("PSMain", "ps_5_0"));
        _layout = device.CreateInputLayout(
            [
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            ],
            bytecode);
        _constants = device.CreateBuffer(
            (uint)Marshal.SizeOf<FrameConstants>(),
            BindFlags.ConstantBuffer,
            ResourceUsage.Dynamic,
            CpuAccessFlags.Write);
        _depthState = device.CreateDepthStencilState(DepthStencilDescription.Default);
        var solid = new RasterizerDescription(CullMode.Back, FillMode.Solid)
        {
            FrontCounterClockwise = true,
            DepthClipEnable = true,
        };
        var wire = new RasterizerDescription(CullMode.Back, FillMode.Wireframe)
        {
            FrontCounterClockwise = true,
            DepthClipEnable = true,
        };
        _rasterizer = device.CreateRasterizerState(solid);
        _wireRasterizer = device.CreateRasterizerState(wire);
    }

    public static SceneRenderer Create()
    {
        if (TryCreate(DriverType.Hardware, out var renderer, out var error))
        {
            return renderer!;
        }

        if (TryCreate(DriverType.Warp, out renderer, out error))
        {
            return renderer!;
        }

        throw new InvalidOperationException(error);
    }

    public void SetMesh(MeshData mesh)
    {
        _vertices?.Dispose();
        _indices?.Dispose();
        _vertices = _device.CreateBuffer(mesh.Vertices, BindFlags.VertexBuffer);
        _indices = _device.CreateBuffer(mesh.Indices, BindFlags.IndexBuffer);
        _indexCount = mesh.Indices.Length;
    }

    public void Resize(int pixelWidth, int pixelHeight)
    {
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        if (_color != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight)
        {
            return;
        }

        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _colorView?.Dispose();
        _color?.Dispose();
        _depthView?.Dispose();
        _depth?.Dispose();

        _color = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)pixelWidth,
            Height = (uint)pixelHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            MiscFlags = ResourceOptionFlags.Shared,
        });
        _colorView = _device.CreateRenderTargetView(_color);
        _depth = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)pixelWidth,
            Height = (uint)pixelHeight,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.D24_UNorm_S8_UInt,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.DepthStencil,
        });
        _depthView = _device.CreateDepthStencilView(_depth);
    }

    public void Render(in OrbitCamera camera, in Vector3 scale, float meshRadius)
    {
        if (_colorView == null || _depthView == null)
        {
            return;
        }

        float aspect = _pixelWidth / (float)_pixelHeight;
        var world = Matrix4x4.CreateScale(scale);
        var view = Matrix4x4.CreateLookAt(camera.Eye, camera.Target, Vector3.UnitY);
        float near = MathF.Max(0.01f, camera.Distance * 0.01f);
        float far = MathF.Max(near + 1f, camera.Distance + meshRadius * 8f);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.FieldOfView, aspect, near, far);
        Matrix4x4.Invert(world, out var inverseWorld);
        var constants = new FrameConstants
        {
            WorldViewProj = Matrix4x4.Transpose(world * view * projection),
            NormalWorld = Matrix4x4.Transpose(inverseWorld),
            LightDirection = Vector3.Normalize(new Vector3(0.35f, 0.8f, 0.5f)),
        };
        var mapped = _context.Map(_constants, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.StructureToPtr(constants, mapped.DataPointer, false);
        _context.Unmap(_constants, 0);

        _context.ClearRenderTargetView(_colorView, new Color4(0.11f, 0.12f, 0.14f, 1f));
        _context.ClearDepthStencilView(_depthView, DepthStencilClearFlags.Depth, 1f, 0);
        _targets[0] = _colorView;
        _context.OMSetRenderTargets(1, _targets, _depthView);
        _viewports[0] = new Viewport(0, 0, _pixelWidth, _pixelHeight);
        _context.RSSetViewports(1, _viewports);
        _context.OMSetDepthStencilState(_depthState, 0);
        _context.RSSetState(Wireframe ? _wireRasterizer : _rasterizer);
        _context.IASetInputLayout(_layout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _constantSlot[0] = _constants;
        _context.VSSetConstantBuffers(0, 1, _constantSlot);
        _context.PSSetConstantBuffers(0, 1, _constantSlot);
        if (_vertices != null && _indices != null && _indexCount > 0)
        {
            _vertexSlot[0] = _vertices;
            _stride[0] = (uint)Marshal.SizeOf<Vertex>();
            _offset[0] = 0;
            _context.IASetVertexBuffers(0, 1, _vertexSlot, _stride, _offset);
            _context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);
            _context.DrawIndexed((uint)_indexCount, 0, 0);
        }

        _context.Flush();
    }

    public void Dispose()
    {
        _vertices?.Dispose();
        _indices?.Dispose();
        _colorView?.Dispose();
        _color?.Dispose();
        _depthView?.Dispose();
        _depth?.Dispose();
        _constants.Dispose();
        _layout.Dispose();
        _vertexShader.Dispose();
        _pixelShader.Dispose();
        _depthState.Dispose();
        _rasterizer.Dispose();
        _wireRasterizer.Dispose();
        _context.Dispose();
        _device.Dispose();
    }

    static bool TryCreate(DriverType driver, out SceneRenderer? renderer, out string error)
    {
        renderer = null;
        ID3D11Device device;
        try
        {
            device = D3D11.D3D11CreateDevice(driver, DeviceCreationFlags.BgraSupport, FeatureLevel.Level_11_0);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        renderer = new SceneRenderer(device, device.ImmediateContext, driver == DriverType.Warp ? "WARP" : "D3D11");
        error = "";
        return true;
    }

    static byte[] Compile(string entry, string profile)
    {
        var source = System.Text.Encoding.UTF8.GetBytes(ShaderSource);
        int hr = D3DCompile(source, (nuint)source.Length, "mesh.hlsl", IntPtr.Zero, IntPtr.Zero, entry, profile, 0, 0, out var code, out var errors);
        try
        {
            if (hr < 0 || code == IntPtr.Zero)
            {
                var message = errors == IntPtr.Zero ? "着色器编译失败。" : Marshal.PtrToStringAnsi(BlobPointer(errors));
                throw new InvalidOperationException(message);
            }

            var bytes = new byte[BlobSize(code)];
            Marshal.Copy(BlobPointer(code), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (code != IntPtr.Zero)
            {
                BlobRelease(code);
            }

            if (errors != IntPtr.Zero)
            {
                BlobRelease(errors);
            }
        }
    }

    static IntPtr BlobPointer(IntPtr blob)
    {
        var method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(blob), 3 * IntPtr.Size);
        return Marshal.GetDelegateForFunctionPointer<BlobPointerDelegate>(method)(blob);
    }

    static int BlobSize(IntPtr blob)
    {
        var method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(blob), 4 * IntPtr.Size);
        return (int)Marshal.GetDelegateForFunctionPointer<BlobSizeDelegate>(method)(blob);
    }

    static void BlobRelease(IntPtr blob)
    {
        var method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(blob), 2 * IntPtr.Size);
        Marshal.GetDelegateForFunctionPointer<BlobReleaseDelegate>(method)(blob);
    }

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    static extern int D3DCompile(
        byte[] source,
        nuint sourceSize,
        string name,
        IntPtr defines,
        IntPtr include,
        string entry,
        string profile,
        uint flags1,
        uint flags2,
        out IntPtr code,
        out IntPtr errors);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate IntPtr BlobPointerDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate nuint BlobSizeDelegate(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    delegate uint BlobReleaseDelegate(IntPtr self);

    [StructLayout(LayoutKind.Sequential)]
    struct FrameConstants
    {
        public Matrix4x4 WorldViewProj;
        public Matrix4x4 NormalWorld;
        public Vector3 LightDirection;
        public float Pad;
    }
}
