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
            float Unlit;
            float Highlight;
            float3 Padding;
        };

        Texture2D BaseColorMap : register(t0);
        SamplerState BaseColorSampler : register(s0);

        struct VsIn
        {
            float3 Position : POSITION;
            float3 Normal : NORMAL;
            float2 TexCoord : TEXCOORD0;
            float4 Color : COLOR0;
        };

        struct VsOut
        {
            float4 Position : SV_Position;
            float3 Normal : TEXCOORD0;
            float2 TexCoord : TEXCOORD1;
            float4 Color : COLOR0;
        };

        VsOut VSMain(VsIn input)
        {
            VsOut output;
            output.Position = mul(WorldViewProj, float4(input.Position, 1));
            output.Normal = mul((float3x3)NormalWorld, input.Normal);
            output.TexCoord = input.TexCoord;
            output.Color = input.Color;
            return output;
        }

        float4 PSMain(VsOut input, bool frontFace : SV_IsFrontFace) : SV_Target
        {
            if (Unlit > 0.5)
            {
                return input.Color;
            }

            float3 normal = normalize(input.Normal);
            if (!frontFace)
            {
                normal = -normal;
            }

            // 主光跟着相机（右上方斜照），再加天空 / 地面半球环境光，背光面也不会比背景还黑。
            float key = saturate(dot(normal, LightDirection));
            float hemisphere = lerp(0.32, 0.55, normal.y * 0.5 + 0.5);
            float4 albedo = BaseColorMap.Sample(BaseColorSampler, input.TexCoord) * input.Color;
            float3 color = albedo.rgb * (hemisphere + 0.75 * key);
            // 选中高亮：往橙色混一部分，贴图模型 / 线框下也看得出来。
            color = lerp(color, float3(1.0, 0.62, 0.18), Highlight * 0.45);
            return float4(saturate(color), 1);
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
    readonly ID3D11RasterizerState _doubleSidedRasterizer;
    readonly ID3D11RasterizerState _doubleSidedWireRasterizer;
    readonly ID3D11SamplerState _sampler;
    readonly ID3D11Texture2D _whiteTexture;
    readonly ID3D11ShaderResourceView _whiteView;
    readonly List<GpuObject> _objects = [];
    ID3D11Buffer? _gridVertices;
    uint _gridVertexCount;
    readonly ID3D11ShaderResourceView[] _textureSlot = new ID3D11ShaderResourceView[1];
    readonly ID3D11SamplerState[] _samplerSlot = new ID3D11SamplerState[1];
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
    int _pixelWidth = 1;
    int _pixelHeight = 1;

    public ID3D11Texture2D? ColorTexture => _color;
    public string DeviceName { get; }
    public bool Wireframe { get; set; }

    /// <summary>当前高亮的对象 id，-1 表示无。</summary>
    public int SelectedId { get; set; } = -1;

    public int ObjectCount => _objects.Count;

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
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0),
                new InputElementDescription("COLOR", 0, Format.R8G8B8A8_UNorm, 32, 0),
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
        _doubleSidedRasterizer = device.CreateRasterizerState(solid with { CullMode = CullMode.None });
        _doubleSidedWireRasterizer = device.CreateRasterizerState(wire with { CullMode = CullMode.None });
        _sampler = device.CreateSamplerState(new SamplerDescription(
            Filter.MinMagMipLinear,
            TextureAddressMode.Wrap,
            TextureAddressMode.Wrap,
            TextureAddressMode.Wrap));
        (_whiteTexture, _whiteView) = CreateTexture(1, 1, [255, 255, 255, 255]);
    }

    unsafe (ID3D11Texture2D Texture, ID3D11ShaderResourceView View) CreateTexture(int width, int height, byte[] bgra)
    {
        fixed (byte* pixels = bgra)
        {
            var texture = _device.CreateTexture2D(
                new Texture2DDescription
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Immutable,
                    BindFlags = BindFlags.ShaderResource,
                },
                new SubresourceData((IntPtr)pixels, (uint)(width * 4)));
            return (texture, _device.CreateShaderResourceView(texture));
        }
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

    /// <summary>上传一个对象的顶点 / 索引 / 贴图（加载时调用一次，不在渲染循环里）。同 id 已存在时先替换。</summary>
    public void AddMesh(int id, MeshData mesh)
    {
        RemoveMesh(id);
        var gpu = new GpuObject
        {
            Id = id,
            Vertices = _device.CreateBuffer(mesh.Vertices, BindFlags.VertexBuffer),
            Indices = _device.CreateBuffer(mesh.Indices, BindFlags.IndexBuffer),
            IndexCount = mesh.Indices.Length,
            Parts = mesh.Parts.Length > 0
                ? mesh.Parts
                : [new MeshPart { StartIndex = 0, IndexCount = mesh.Indices.Length }],
        };
        foreach (var texture in mesh.Textures)
        {
            gpu.Textures.Add(CreateTexture(texture.Width, texture.Height, texture.Bgra));
        }

        _objects.Add(gpu);
    }

    /// <summary>释放一个对象的 GPU 资源。</summary>
    public void RemoveMesh(int id)
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            if (_objects[i].Id == id)
            {
                _objects[i].Dispose();
                _objects.RemoveAt(i);
            }
        }
    }

    public void ClearMeshes()
    {
        foreach (var gpu in _objects)
        {
            gpu.Dispose();
        }

        _objects.Clear();
    }

    /// <summary>地面网格按整个场景的包围盒重建（对象增删后由视口调用）。</summary>
    public void UpdateGrid(Vector3 sceneMin, Vector3 sceneMax) => BuildGrid(sceneMin, sceneMax);

    /// <summary>兼容旧调用：清空场景只放这一个网格（id 0）。</summary>
    public void SetMesh(MeshData mesh)
    {
        ClearMeshes();
        AddMesh(0, mesh);
        BuildGrid(mesh.Min, mesh.Max);
    }

    /// <summary>
    /// Blender 式地面网格：在 y = 0 平面上，红线是 X 轴，绿线是 Z 轴（对应 Blender 的 Y 轴）。
    /// 格子间距取 10 的整数次幂，按模型大小自动选，范围盖住模型。
    /// </summary>
    void BuildGrid(Vector3 min, Vector3 max)
    {
        _gridVertices?.Dispose();
        _gridVertices = null;
        float size = MathF.Max(MathF.Max(max.X - min.X, max.Z - min.Z), max.Y - min.Y);
        size = MathF.Max(size, 1e-4f);
        float step = MathF.Pow(10f, MathF.Floor(MathF.Log10(size / 4f)));
        float reach = MathF.Max(MathF.Max(MathF.Abs(min.X), MathF.Abs(max.X)), MathF.Max(MathF.Abs(min.Z), MathF.Abs(max.Z)));
        int half = Math.Clamp((int)MathF.Ceiling(MathF.Max(reach, size) * 1.5f / step), 5, 400);
        float extent = half * step;
        uint minor = Vertex.PackColor(new Vector4(0.30f, 0.31f, 0.33f, 1f));
        uint major = Vertex.PackColor(new Vector4(0.38f, 0.39f, 0.41f, 1f));
        uint red = Vertex.PackColor(new Vector4(0.80f, 0.25f, 0.28f, 1f));
        uint green = Vertex.PackColor(new Vector4(0.45f, 0.70f, 0.20f, 1f));
        var lines = new List<Vertex>((half * 2 + 1) * 4);
        for (int i = -half; i <= half; i++)
        {
            float t = i * step;
            uint along = i == 0 ? red : (i % 10 == 0 ? major : minor);
            uint across = i == 0 ? green : (i % 10 == 0 ? major : minor);
            // 平行于 X 的线（z = t），z = 0 那条就是红色 X 轴。
            lines.Add(new Vertex(new Vector3(-extent, 0, t), Vector3.UnitY) { Color = along });
            lines.Add(new Vertex(new Vector3(extent, 0, t), Vector3.UnitY) { Color = along });
            // 平行于 Z 的线（x = t），x = 0 那条就是绿色轴。
            lines.Add(new Vertex(new Vector3(t, 0, -extent), Vector3.UnitY) { Color = across });
            lines.Add(new Vertex(new Vector3(t, 0, extent), Vector3.UnitY) { Color = across });
        }

        var array = lines.ToArray();
        _gridVertices = _device.CreateBuffer(array, BindFlags.VertexBuffer);
        _gridVertexCount = (uint)array.Length;
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

    public bool ShowGrid { get; set; } = true;

    /// <summary>渲染和拾取共用同一套 view / projection（含正交），保证点到哪选到哪。</summary>
    public static void GetViewProjection(in OrbitCamera camera, in Vector3 scale, float meshRadius, float aspect, out Matrix4x4 view, out Matrix4x4 projection)
    {
        var eye = camera.Eye;
        view = Matrix4x4.CreateLookAt(eye, camera.Target, Vector3.UnitY);
        // 近 / 远平面跟着相机距离和模型大小走，深度精度够用，也不会把模型裁掉。
        float scaledRadius = meshRadius * MathF.Max(scale.X, MathF.Max(scale.Y, scale.Z));
        float near = MathF.Max(camera.Distance * 0.002f, (camera.Distance - scaledRadius * 4f) * 0.5f);
        float far = camera.Distance + scaledRadius * 20f + 1e-3f;
        if (camera.Orthographic)
        {
            float halfHeight = camera.OrthoHalfHeight;
            projection = Matrix4x4.CreateOrthographic(halfHeight * 2f * aspect, halfHeight * 2f, -far, far);
        }
        else
        {
            projection = Matrix4x4.CreatePerspectiveFieldOfView(camera.FieldOfView, aspect, near, far);
        }
    }

    public void Render(in OrbitCamera camera, in Vector3 scale, float meshRadius)
    {
        if (_colorView == null || _depthView == null)
        {
            return;
        }

        float aspect = _pixelWidth / (float)_pixelHeight;
        var eye = camera.Eye;
        GetViewProjection(camera, scale, meshRadius, aspect, out var view, out var projection);

        // 主光从相机右上方照过来：随视角转，任何角度看过去正面都是亮的。
        var forward = Vector3.Normalize(camera.Target - eye);
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var light = Vector3.Normalize(-forward + right * 0.45f + up * 0.75f);
        var viewProjection = view * projection;

        _context.ClearRenderTargetView(_colorView, new Color4(0.11f, 0.12f, 0.14f, 1f));
        _context.ClearDepthStencilView(_depthView, DepthStencilClearFlags.Depth, 1f, 0);
        _targets[0] = _colorView;
        _context.OMSetRenderTargets(1, _targets, _depthView);
        _viewports[0] = new Viewport(0, 0, _pixelWidth, _pixelHeight);
        _context.RSSetViewports(1, _viewports);
        _context.OMSetDepthStencilState(_depthState, 0);
        _context.IASetInputLayout(_layout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.VSSetShader(_vertexShader);
        _context.PSSetShader(_pixelShader);
        _constantSlot[0] = _constants;
        _context.VSSetConstantBuffers(0, 1, _constantSlot);
        _context.PSSetConstantBuffers(0, 1, _constantSlot);
        _stride[0] = (uint)Marshal.SizeOf<Vertex>();
        _offset[0] = 0;
        if (ShowGrid && _gridVertices != null)
        {
            WriteConstants(Matrix4x4.Identity, viewProjection, light, unlit: true, highlight: false);
            _context.RSSetState(_doubleSidedRasterizer);
            _context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
            _vertexSlot[0] = _gridVertices;
            _context.IASetVertexBuffers(0, 1, _vertexSlot, _stride, _offset);
            _textureSlot[0] = _whiteView;
            _context.PSSetShaderResources(0, 1, _textureSlot);
            _context.Draw(_gridVertexCount, 0);
            _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        }

        _samplerSlot[0] = _sampler;
        _context.PSSetSamplers(0, 1, _samplerSlot);
        var world = Matrix4x4.CreateScale(scale);
        // for 循环 + 预分配的槽数组：渲染循环里不产生托管分配。
        for (int o = 0; o < _objects.Count; o++)
        {
            var gpu = _objects[o];
            if (gpu.IndexCount <= 0)
            {
                continue;
            }

            WriteConstants(world, viewProjection, light, unlit: false, highlight: gpu.Id == SelectedId);
            _vertexSlot[0] = gpu.Vertices;
            _context.IASetVertexBuffers(0, 1, _vertexSlot, _stride, _offset);
            _context.IASetIndexBuffer(gpu.Indices, Format.R32_UInt, 0);
            var parts = gpu.Parts;
            for (int p = 0; p < parts.Length; p++)
            {
                var part = parts[p];
                if (part.IndexCount <= 0 || part.StartIndex + part.IndexCount > gpu.IndexCount)
                {
                    continue;
                }

                _context.RSSetState(part.DoubleSided
                    ? (Wireframe ? _doubleSidedWireRasterizer : _doubleSidedRasterizer)
                    : (Wireframe ? _wireRasterizer : _rasterizer));
                _textureSlot[0] = part.TextureIndex >= 0 && part.TextureIndex < gpu.Textures.Count
                    ? gpu.Textures[part.TextureIndex].View
                    : _whiteView;
                _context.PSSetShaderResources(0, 1, _textureSlot);
                _context.DrawIndexed((uint)part.IndexCount, (uint)part.StartIndex, 0);
            }
        }

        _context.Flush();
    }

    /// <summary>直接写进映射的常量缓冲，不经过 Marshal.StructureToPtr（它会装箱，每帧产生垃圾）。</summary>
    unsafe void WriteConstants(in Matrix4x4 world, in Matrix4x4 viewProjection, Vector3 light, bool unlit, bool highlight)
    {
        Matrix4x4.Invert(world, out var inverseWorld);
        var mapped = _context.Map(_constants, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        var constants = (FrameConstants*)mapped.DataPointer;
        // System.Numerics 是行向量约定（v * M）；HLSL 常量缓冲默认按列主序读取，配合 mul(M, v)
        // 正好等价于 v * M，所以 WorldViewProj 直接上传、不转置。转置 + mul(M, v) 会让投影错乱（之前视口全黑的原因）。
        constants->WorldViewProj = world * viewProjection;
        constants->NormalWorld = Matrix4x4.Transpose(inverseWorld);
        constants->LightDirection = light;
        constants->Unlit = unlit ? 1f : 0f;
        constants->Highlight = highlight ? 1f : 0f;
        constants->Padding = Vector3.Zero;
        _context.Unmap(_constants, 0);
    }

    ID3D11Query? _gpuIdleQuery;

    /// <summary>等 GPU 把已提交的命令全部做完（只给性能测试用，正常渲染不调用）。</summary>
    public void WaitForGpu()
    {
        _gpuIdleQuery ??= _device.CreateQuery(new QueryDescription(QueryType.Event));
        _context.End(_gpuIdleQuery);
        _context.Flush();
        while (!_context.IsDataAvailable(_gpuIdleQuery))
        {
            Thread.SpinWait(64);
        }
    }

    public string AdapterName
    {
        get
        {
            try
            {
                using var dxgiDevice = _device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();
                return adapter.Description.Description;
            }
            catch (Exception)
            {
                return DeviceName;
            }
        }
    }

    public void Dispose()
    {
        ClearMeshes();
        _gpuIdleQuery?.Dispose();
        _gridVertices?.Dispose();
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
        _doubleSidedRasterizer.Dispose();
        _doubleSidedWireRasterizer.Dispose();
        _whiteView.Dispose();
        _whiteTexture.Dispose();
        _sampler.Dispose();
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
        public float Unlit;
        public float Highlight;
        public Vector3 Padding;
    }

    /// <summary>一个场景对象的 GPU 资源：顶点 / 索引缓冲 + 贴图。</summary>
    sealed class GpuObject : IDisposable
    {
        public int Id;
        public required ID3D11Buffer Vertices;
        public required ID3D11Buffer Indices;
        public int IndexCount;
        public MeshPart[] Parts = [];
        public readonly List<(ID3D11Texture2D Texture, ID3D11ShaderResourceView View)> Textures = [];

        public void Dispose()
        {
            foreach (var (texture, view) in Textures)
            {
                view.Dispose();
                texture.Dispose();
            }

            Textures.Clear();
            Vertices.Dispose();
            Indices.Dispose();
        }
    }
}
