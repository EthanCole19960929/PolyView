# AGENTS.md — 给接手 PolyView 的 AI 代理

> 本文是 `docs\` 的总索引，也是接手说明。所有内容以仓库里的实际代码为准；和代码对不上时，以代码为准并顺手改这里。

## 项目目标

PolyView 是一个用 C# 写的轻量 Windows 3D 模型查看器：WPF 做界面，Direct3D 11 渲染，画面通过 `D3DImage` 嵌进 WPF。第一期只做「能打开模型、能转能看、能选中」，不做建模、动画编辑和离线渲染。完整规划见 [01_开发计划说明.md](01_开发计划说明.md)。

## 文档索引

| 编号 | 文件 | 作者 | 日期 | 一句话说明 |
| --- | --- | --- | --- | --- |
| 01 | [01_开发计划说明.md](01_开发计划说明.md) | Ethan | 2026-10-23 | 技术选型、架构、分阶段计划和风险 |
| 02 | [02_动作资源网站.md](02_动作资源网站.md) | Ethan | 2026-10-23 | 骨骼动作素材网站（Mixamo 等）和使用限制 |
| 03 | [03_3D显示原理与优化.md](03_3D显示原理与优化.md) | 小U | 2026-10-23 | 3D 显示原理与优化草稿（容量数字为估算） |
| 04 | [04_渲染原理与优化.md](04_渲染原理与优化.md) | 小B | 2026-10-23 | 按实际代码写的渲染流程、相机和光照、已做 / 待做优化、实测容量、格式和 Draco 说明 |
| — | [AGENTS.md](AGENTS.md) | 小B | 2026-10-23 | 本文：接手说明和文档索引 |

**规则：** 新的 Markdown 文档一律放进 `docs\`，文件名用下一个两位编号 `NN_标题.md`，并且必须加进上面这张表。只有 `AGENTS.md` 不编号。

## 技术栈（以 PolyView.csproj 为准）

| 部分 | 版本 / 选择 |
| --- | --- |
| 运行时 | .NET 10，`net10.0-windows`，WPF（代码后置，没有 MVVM） |
| 渲染 | Vortice.Direct3D11 3.8.3、Vortice.Direct3D9 3.8.3（D3D9Ex 只做 D3DImage 共享表面）、Vortice.D3DCompiler 3.8.3（实际用 P/Invoke 调 `d3dcompiler_47.dll` 运行时编译 HLSL） |
| glTF / GLB | SharpGLTF.Core 1.0.7 |
| FBX / DAE / OBJ+MTL / 3DS / PLY | Silk.NET.Assimp 2.23.0（自带原生 assimp 6.0.2，`runtimes\win-x64\native`） |
| Draco（GLB 的 KHR_draco_mesh_compression） | Google Draco 1.5.7 解码库，Apache-2.0，从源码交叉编译的 `native\win-x64\draco_decoder.dll`，P/Invoke 调用（来源和编译方法见同目录 README.txt） |
| three.js JSON | 自己写的解析器（`ThreeJsImporter.cs`，用 System.Text.Json） |
| STL | 自己写的解析器（`MeshImporter.cs`） |
| 数学 | `System.Numerics`（行向量约定，右手系，Y 轴朝上） |

## 编译和运行

```powershell
cd F:\project\c#\2026\1023_PolyView
dotnet build PolyView\PolyView\PolyView.csproj
dotnet run --project PolyView\PolyView\PolyView.csproj
# 直接打开某个模型（也用于「打开方式」）：
PolyView\PolyView\bin\Debug\net10.0-windows\PolyView.exe Models\Knight.glb
```

Ethan 可能开着 `PolyView.exe`，编译报文件被占用时先告诉他，不要直接结束他的进程。

## 目录

```
1023_PolyView/
├── README.md / README.zh-CN.md     英文 / 中文说明
├── .gitignore                      忽略 bin/ obj/ .vs/ *.user /_旧版备份/
├── docs/                           所有文档（见上面的索引）
├── Models/                         测试模型，平铺在一层，来源和授权见 Models/SOURCE.txt
└── PolyView/
    ├── PolyView.slnx               Visual Studio 2026 解决方案
    └── PolyView/
        ├── PolyView.csproj         项目文件和 NuGet 引用
        ├── App.xaml(.cs)           WPF 入口
        ├── MainWindow.xaml(.cs)    主窗口：工具条、状态栏、打开对话框、拖放、命令行打开
        ├── ModelViewport.cs        视口控件：D3DImage + D3D9Ex 共享表面、鼠标键盘导航、后台加载
        ├── SceneRenderer.cs        D3D11 设备、HLSL 着色器、顶点 / 索引缓冲、贴图、绘制
        ├── OrbitCamera.cs          环绕相机：目标点、偏航、俯仰、距离、取景
        ├── MeshData.cs             Vertex / MeshPart / MeshTexture / MeshData / ImportResult
        ├── MeshImporter.cs         按扩展名分派导入；自带 OBJ / STL 解析
        ├── GltfImporter.cs         SharpGLTF 读 glTF / GLB：节点层级变换、蒙皮按绑定姿态、贴图
        ├── DracoGlb.cs             GLB 里的 Draco 压缩网格：调 draco_decoder.dll 解码后重写成普通 GLB
        ├── AssimpImporter.cs       Assimp 读 FBX / DAE / OBJ+MTL / 3DS / PLY，含 FBX 内嵌贴图
        ├── ThreeJsImporter.cs      three.js JSON（Object / Scene 4.x）
        ├── TextureDecoder.cs       用 WPF / WIC 把 PNG / JPEG / WebP 解码成 BGRA32（没有解码器时跳过）
        ├── MeshBuilder.cs          内置示例长方体 / 网格，导出 OBJ / STL
        ├── UiText.cs               中英文界面文字
        └── native/win-x64/         draco_decoder.dll + 许可证 + 来源说明（编译时复制到输出目录）
```

本地备份目录 `_旧版备份\` 已加入 `.gitignore`，不要提交。

## 渲染流程（摘要）

文件 → 导入器（SharpGLTF / Assimp / 自带 OBJ-STL）→ `MeshData`（世界坐标顶点 + 32 位索引 + 分段材质）→ D3D11 顶点 / 索引缓冲 → HLSL（顶点着色器做 WorldViewProj，像素着色器做贴图 × 顶点色 × 光照）→ 带共享标志的 D3D11 纹理 → D3D9Ex 打开同一张纹理 → `D3DImage` → WPF 合成。详细说明和实测数据见 [04_渲染原理与优化.md](04_渲染原理与优化.md)。

## 支持的格式

「已支持（master）」表示已合并进 master；「已支持（feat/import-xiaoB）」表示只在小B的分支上，等 Ethan 合并；「开发中」是正在这个分支上写；「计划中」是还没开始。

| 格式 | 导入方式 | 状态 | 说明 |
| --- | --- | --- | --- |
| .glb / .gltf | SharpGLTF（失败时退回 Assimp） | 已支持（master） | 多网格、多图元、节点变换、基础颜色贴图（PNG / JPEG）、顶点色、双面；蒙皮按绑定姿态 |
| .obj (+.mtl) | 自带解析器；分支上改为 Assimp 优先 | 已支持（master）；Assimp 读 MTL 贴图：已支持（feat/import-xiaoB） | 读 MTL 颜色和 map_Kd 贴图 |
| .stl | 自带解析器 | 已支持（master） | 二进制 / ASCII；按惯例 Z 轴朝上，导入时转成 Y 轴朝上（分支上） |
| .fbx / .dae | Assimp | 已支持（feat/import-xiaoB） | 节点变换烘焙进顶点，缺法线自动生成，读漫反射颜色和贴图（含 FBX 内嵌贴图） |
| .3ds / .ply | Assimp | 已支持（feat/import-xiaoB） | 同上，未专门测试 |
| three.js JSON（.json，`toJSON()` / 编辑器导出） | 自写解析器 | 已支持（feat/import-xiaoB） | `metadata.version` 4.x 的 Object / Scene 格式：BufferGeometry（position / normal / uv / color / index）+ Box / Sphere / Cylinder / Plane + 节点 matrix，只取基础颜色；其他几何体跳过并在状态栏说明；不是 three.js 的 .json 给一行提示。多数 three.js 项目发布的是 .glb / .gltf，已经能直接打开 |
| GLB + KHR_draco_mesh_compression | Draco 1.5.7 解码库（`DracoGlb.cs`） | 已支持（feat/import-xiaoB），只支持 .glb | Ethan 的模型图库 54 个 GLB 里有 52 个用了 Draco；.gltf + Draco 会提示请用 .glb；找不到 DLL 时提示「该 GLB 使用 Draco 压缩…暂不支持」 |
| GLB + EXT_texture_webp | SharpGLTF + Windows WIC 解码 | 已支持（feat/import-xiaoB） | 系统有 WebP 解码器（Windows 10 / 11 一般自带）时显示贴图，在 Ethan 电脑上实测正常；没有解码器时安全跳过，只显示基础颜色 |

## 操作（Blender 风格）

| 操作 | 按键 |
| --- | --- |
| 环绕 | 中键拖动；没有中键时 Alt + 左键拖动（Alt + Shift + 左键平移）；普通左键留给选择 |
| 平移 | Shift + 中键拖动 |
| 缩放 | 滚轮（朝目标点）；Ctrl + 中键上下拖动 |
| 框选全部 | Home 或小键盘 `.` |
| 前 / 右 / 顶视图 | 小键盘 1 / 3 / 7；加 Ctrl 看反面 |
| 透视 / 正交切换 | 小键盘 5 |
| 打开模型 | 「打开模型」按钮、把文件拖到窗口任意位置，或命令行参数 |

（状态：已支持（feat/import-xiaoB）；master 上是左键环绕、中键平移、滚轮缩放。）

命令行性能测试：`PolyView.exe --bench <模型> <输出.tsv>`，加载、测 FPS 和 GPU 每帧耗时，追加一行 TSV 后退出。说明和实测结果见 [04_渲染原理与优化.md](04_渲染原理与优化.md)。

## 文件归属

| 谁 | 负责的文件 / 目录 |
| --- | --- |
| 小B | `MeshImporter.cs`、`GltfImporter.cs`、`TextureDecoder.cs`、`MainWindow.xaml(.cs)`、`ModelViewport.cs`、`SceneRenderer.cs`、`OrbitCamera.cs`、`UiText.cs`、`PolyView.csproj`、`Models/`、`samples/`。交接给小U之前由小B负责 |
| 小U | 交接后负责视口、核心、相机和拾取（`ModelViewport.cs`、`SceneRenderer.cs`、`OrbitCamera.cs` 以及以后的 Core / 拾取代码） |
| 小I | 只动 `assets/` |
| 小C | 只做审查，结论写进 `审查.md` |
| 小M | 以后负责骨骼动画样例 |
| Ethan | 合并到 master |

改别人负责的文件前，先在群里说一声。

## Git 规则

- 每个代理推自己的分支：`feat/<主题>-<名字>`，例如 `feat/import-xiaoB`。
- 不推 master，不强推（不用 `--force`）。合并到 master 由 Ethan 做。
- 推之前先 `git pull --rebase`，编译必须通过。
- 提交信息用 `[名字]` 开头，例如 `[小B] GLB/FBX/DAE 导入`。
- 不提交编译产物：`.gitignore` 已忽略 `bin/`、`obj/`、`.vs/`、`*.user`、`/_旧版备份/`。注意 `[Oo]bj/` 也会忽略名叫 `obj` 的模型目录，所以 `Models/` 保持平铺。
- 本机推送 GitHub 若报 TLS 错误，用 `git -c http.sslBackend=schannel push ...`。

## 当前进度（2026-10-23）

- 已在 master：OBJ / STL / GLB / glTF 导入，拖放打开，命令行打开，修复视口全黑（矩阵转置），中英文切换。
- `feat/import-xiaoB` 分支（已推送，等 Ethan 合并）：Models 平铺（bf3fd7b）、文档整理（c6a515c）、合并 master（e7a1015）；代码提交：Assimp 导入 FBX / DAE、three.js JSON、GLB 的 Draco 解码和 WebP 贴图、Blender 式导航、地面网格、按需渲染、去掉「X 拉伸」和「缩放」滑块、去掉界面上的 FPS 显示、相机跟随光照、`--bench` 性能测试、docs/04。
- 下一步：小C 按下面的验收标准逐项验收；内存反复开关测试；拾取和场景树（第 2 阶段）交给小U。

## 验收标准与已知坑

### 渲染
- 矩阵：System.Numerics 是行主序，HLSL 默认列主序。上传常量缓冲前要 Transpose，或者在 HLSL 里声明 row_major。漏了这一步，画面会歪斜、变形。
- 宽高比取后备缓冲的实际像素，不要用 WPF 的 DIP 尺寸，否则在 150% DPI 下画面会被压扁。
- 右手系矩阵要配 FrontCounterClockwise=true，并且要有深度缓冲。
- 文件没带法线时要按面自动算。光照要有环境光，模型最暗的地方也要比背景亮。
- 打开模型后，相机对准包围盒中心，距离按包围半径来算。
### D3DImage
- 交给 D3DImage 之前先 Flush。
- 要处理 IsFrontBufferAvailableChanged，前缓冲丢失后要能恢复，不能变成黑屏。
### 交互
- 拖拽：AllowDrop=True，Background=Transparent，在 DragOver 里把 Effects 设成 Copy，读取 DataFormats.FileDrop。以管理员身份运行时 Windows 会拦截拖拽，属于正常现象。
- 打开对话框的过滤器要包含 *.glb;*.gltf。
- 遇到不支持的格式，在状态栏提示，不能让程序崩溃。
- 「复位视角」要把相机恢复成默认值（缩放为 1）。
### FPS
- 界面上不显示 FPS（按用户要求已去掉）。需要看帧率和 GPU 耗时时用 `--bench`。渲染只在需要时进行，静止时不占 GPU。
### 每次提交的验收
- Models 里的每个模型都能打开，也能拖进窗口。同一个模型的几种格式打开后，亮度、大小、朝向要一致。
- 窗口缩放时模型不能变形。150% DPI 下画面要清晰。
- 反复打开、关闭 10 到 20 次，内存要回到起点。渲染循环里不能分配新内存，关闭时所有资源都要 Dispose。
### Git
- 各人推自己的分支，禁止强推。直接推 master 要先问 Ethan。推之前先 pull --rebase，编译不过不推。

### 实际踩过的坑（小B）

- **矩阵转置：** 最初代码对 WorldViewProj 做了 `Transpose`，HLSL 里又用 `mul(M, v)`，两次「修正」叠在一起，整个模型投影到画面外，视口全黑。现在的约定是：C# 不转置，HLSL 用默认列主序 + `mul(M, v)`（等价于行向量的 `v * M`）。二选一，不能两个都做。
- **宽高比：** 用 `ActualWidth × DpiScaleX` 算出的后备缓冲像素尺寸，缩放窗口时重建共享纹理。
- **绕序和深度：** 右手系 + `FrontCounterClockwise = true`；glTF 的双面材质关掉背面剔除，像素着色器用 `SV_IsFrontFace` 翻转法线。负缩放节点要交换三角形顶点顺序。
- **STL 朝向：** STL 按惯例 Z 轴朝上，直接显示会躺倒，导入时转成 Y 轴朝上。
- **拖放被拦截：** 以管理员身份运行时，从普通权限的资源管理器拖文件进来会被 Windows（UIPI）拦截，光标一直是禁止符号。用普通权限运行即可。视口是 `D3DImage` 不是 `HwndHost`，没有空气墙，拖到 3D 区域也能收到事件。
- **D3DImage：** 每帧画完 `Flush`，再 `Lock / AddDirtyRect / Unlock`；`IsFrontBufferAvailable` 变化（锁屏、远程桌面）后要重新出图。
- **每帧分配：** `Marshal.StructureToPtr` 会装箱，每帧产生垃圾；常量缓冲改成指针直接写。
- **Assimp 原生库：** 自带的 assimp 6.0.2 没有导出 `aiGetEmbeddedTexture`，内嵌贴图要自己按 `*N` 或文件名去 `scene->mTextures` 里找。
- **`[Oo]bj/` 忽略规则：** 会把名叫 `obj` 的模型子目录也忽略掉，之前 `watertower.obj` 因此没进仓库。
- **FPS：** `CompositionTarget.Rendering` 跟着显示器刷新率走，帧率上限约等于刷新率；只在需要时渲染。界面已不再显示 FPS。
- **Draco 解码后的 min / max：** Draco 是量化压缩，解出来的坐标会略微超出 accessor 原来写的 min / max，SharpGLTF 读取时会校验并报错，所以重写 GLB 时要按解码结果重新算 min / max。
- **draco_decoder.dll 依赖：** mingw 编出的 DLL 默认依赖 libgcc_s / libstdc++，Ethan 电脑上没有；CMake 的目标是 MODULE，要用 `CMAKE_MODULE_LINKER_FLAGS="-static -static-libgcc -static-libstdc++"`，编完用 objdump 确认只依赖 KERNEL32 和 msvcrt。
- **FPS 上限：** `--bench` 测到的 vsync FPS 最多约等于显示器刷新率，看 GPU 真实负载用「GPU ms/帧」。
