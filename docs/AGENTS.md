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
| 04 | [04_渲染原理与优化.md](04_渲染原理与优化.md) | 小B、小U | 2026-10-23 | 按实际代码写的渲染流程、相机和光照、已做 / 待做优化、实测容量、格式和 Draco 说明；第 2 阶段多对象 / 拾取 / 高亮及实测、给场景树的接口 |
| 05 | [05_变换操纵轴规格.md](05_变换操纵轴规格.md) | novel | 2026-10-23 | Blender 式选择/移动/旋转/缩放工具与手柄、快捷键、对象 TRS 数据模型、拾取接口、场景大纲、验收清单 |
| 06 | [06_开发进度.md](06_开发进度.md) | program | 2026-10-23 | 已完成 / 进行中 / 计划、分工和创新点（按 master git log） |
| 07 | [07_灯光规格.md](07_灯光规格.md) | novel | 2026-10-23 | 对齐 Blender 的灯光类型（Point/Sun/Spot/Area）与 World 环境光、glTF/Assimp 导入映射、视口图标、着色模式、数据模型、分工与验收 |
| 08 | [08_开源项目借鉴.md](08_开源项目借鉴.md) | novel | 2026-10-23 | 借鉴 Helix Toolkit / Stride / ImGuizmo / Flax 源码：选中描边、无限网格遮挡、手柄几何与拖拽数学、撤销/重做、大纲树、灯光图标、BVH 拾取、吸附，按 P0/P1/P2 对到 PolyView 类和负责人 |
| 09 | [09_工程文件格式.md](09_工程文件格式.md) | program | 2026-10-23 | .pvproj 工程文件（zip + project.json）设计：保存/复原内容、保存打开流程、版本兼容、实现计划 |
| 10 | [10_渲染实现与性能.md](10_渲染实现与性能.md) | program | 2026-10-23 | 每帧渲染流程、着色模式与光照公式、和 Blender Workbench/Eevee/Cycles 对比、CPU/GPU 内存推算、风险和后续优化 |
| 11 | [11_MCP接入设计.md](11_MCP接入设计.md) | program | 2026-10-23 | AI 通过 MCP 操控 PolyView：架构对比与推荐、线程/撤销/安全、工具与资源、客户端配置、分阶段计划、AI 可调用原则 |
| 12 | [12_Blender联动设计.md](12_Blender联动设计.md) | program | 2026-10-23 | PolyView 与 Blender 联动：一键发送、导入 .blend、双向实时同步（Live Link）、复用 MCP |
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
cd C:\Project\C#\PolyView
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

## 项目特色

- **内置零配置 MCP**：AI 不用额外配置就能操控 PolyView，自动扫描本机 AI 客户端一键接入（见 11）。
- **Blender 联动**：一键发送、导入 .blend、双向实时同步（见 12）。
- **复刻 Blender 4.2**：界面、操作、观感尽量一致。

## 开发原则

- **每个功能都必须注册命令**：新功能必须在 `Core/Commands` 中用 `[PvCommand("类别.名称", ...)]` 注册（参数用 `[Description]` 说明，并标明 Undoable / Destructive）。菜单、快捷键和 MCP 的 `run_command` 都调用同一个命令，AI 不需要额外改动就能用上新功能（见 docs/11 第 15 节）。
- **性能优先**：按需渲染，渲染循环不分配内存，资源及时释放。

## 协作方式（多 bot）

- 用户的需求交给 program，program 拆分并分派给各 bot；其他 bot 也可以写代码，但各自只负责分到的文件和功能块。
- 每次开工先读 `docs/AGENTS.md` 和 `docs/06_开发进度.md`。

## Git 规则

1. 只在 master 开发，不建分支 / worktree。
2. 提交信息用英文，以自己的前缀开头：`[program]`、`[novel]`、`[english]`、`[PolyView软件开发]` 等。
3. 推送前先 `git pull`；被别人未提交的改动挡住时用 `git fetch` + 快进。
4. 禁止 `git stash`、`git reset`、`--force`。
5. 只 `git add <自己的文件>`，禁止 `git add .` / `-A`；提交前先看暂存区。
6. 编译通过才推；不提交 bin/ obj/ .vs/。

## 改完代码后的流程

1. 关闭正在运行的 PolyView。
2. 编译 Debug：`dotnet build PolyView\PolyView\PolyView.csproj`，输出 `PolyView\PolyView\bin\Debug\net10.0-windows\PolyView.exe`。
3. 重新启动新版本（桌面快捷方式 `PolyView.lnk`），确认已运行。崩溃日志在 `%LOCALAPPDATA%\PolyView\crash.log`。

## 当前进度

见 [06_开发进度.md](06_开发进度.md)。
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
- 只用 master 一个分支，禁止强推。推之前先 pull --rebase，编译不过不推。

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
