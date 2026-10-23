# 11 MCP 接入设计（让 AI 操控 PolyView）

> 状态：设计稿，待批准。只有文档，还没写代码。作者：program，2026-10-23

## 1. 目标
让 Claude Desktop、Cursor、Claude Code 这类 AI 客户端通过 **MCP（Model Context Protocol）** 操控**正在运行的** PolyView，可以做这些事：查询场景、导入模型、移动/旋转/缩放、加灯、切换着色模式、选择物体、保存/打开 .pvproj、导出（等导出功能做好后），以及截取视口图片给 AI 看。

## 2. 背景与参考
- **MCP**：JSON-RPC 2.0 协议。服务端提供 *Tools*（可调用的函数）、*Resources*（可读的数据）和 *Prompts*。传输方式有两种：**stdio**（客户端启动子进程，通过标准输入输出通信）和 **Streamable HTTP**（单一 HTTP 端点，可用 SSE 流式返回；它取代了旧的 HTTP+SSE 双端点方式）。
- **官方 C# SDK**：`modelcontextprotocol/csharp-sdk`，和微软合作维护。NuGet 包有：
  - `ModelContextProtocol`：核心库，带 Hosting/DI 扩展，支持 stdio 服务器；
  - `ModelContextProtocol.AspNetCore`：Streamable HTTP 服务器（`MapMcp()`）；
  - `ModelContextProtocol.Core`：只含客户端和底层 API。
  
  用 `[McpServerToolType]` / `[McpServerTool, Description(...)]` 标注静态方法或实例方法，SDK 会自动根据参数生成 JSON Schema。
- **blender-mcp**（ahujasid/blender-mcp）是参考架构：Blender 插件在 Blender 进程里开一个 TCP socket 服务（localhost:9876），收到 JSON 命令后，通过 `bpy.app.timers` 调度到 Blender 主线程执行；另有一个独立的 Python MCP 服务器（`uvx blender-mcp`，用 stdio）负责把 MCP 调用转成 socket 命令。核心工具有 `get_scene_info`、`get_object_info`、`get_viewport_screenshot` 和 `execute_blender_code`（任意代码，风险高）。我们借鉴它的“应用内服务 + 桥接”思路，**但不提供任意代码执行**。

## 3. 架构方案

### 方案 A：PolyView 进程内托管 Streamable HTTP MCP 服务器
```
AI 客户端 ──HTTP(127.0.0.1:47800/mcp)──► PolyView.exe 内的 Kestrel + MapMcp ──Dispatcher──► ModelViewport
```
- 优点：只有一个进程，没有桥接；工具直接调用 C# 对象；实现量最小。
- 缺点：WPF 项目需要引入 ASP.NET Core（`FrameworkReference Microsoft.AspNetCore.App`），体积和启动时间会增加一点；部分客户端（例如旧版 Claude Desktop）只支持 stdio，需要用 `mcp-remote` 转接；PolyView 没开时，AI 连不上。

### 方案 B：独立 stdio 桥接 exe + 命名管道
```
AI 客户端 ──stdio──► PolyView.Mcp.exe（官方 SDK，stdio）──NamedPipe "PolyView.Automation"──► PolyView.exe 内的 AutomationServer ──Dispatcher──► ModelViewport
```
- 优点：所有客户端都原生支持 stdio；PolyView 本体不需要引入 ASP.NET；桥接程序可以在 PolyView 没运行时自动启动它；命名管道可以用 ACL 限制只有当前用户能访问；结构和 blender-mcp 一致，比较成熟。
- 缺点：多一个工程和一套内部协议（JSON 行协议），需要维护两边的命令定义。

### 推荐：**B 为主，A 作为后续可选项**
1. 第一阶段做 B：`PolyView.Mcp`（console，stdio）+ PolyView 内的 `Core/Automation/AutomationServer`（命名管道）。
2. 内部协议直接复用同一组命令 DTO（放在共享工程 `PolyView.Automation.Contracts` 中）。以后要做方案 A 时，只需在 PolyView 内用 `MapMcp` 包装同一个 `AutomationService`，不需要重写工具。
3. 所有工具最后都调用同一个 `AutomationService`。UI 菜单、快捷键和 AI 走同一套命令，保证行为一致（见第 4 节）。

## 4. 原则：功能要设计成“AI 可调用”
以后新增的每个功能都遵守下面几条：
1. **命令和界面分离**：每个操作先写成 `AutomationService` / 命令类里的一个方法（参数是普通类型，返回结构化结果），菜单、快捷键、AI 都调用它，不把逻辑写死在事件处理函数里。
2. **能用 ID 定位**：物体、灯光、节点、集合都用稳定的 `id` 引用，不依赖鼠标位置或当前选中状态。
3. **可序列化**：参数和返回值能转成 JSON（向量用 `[x,y,z]`，角度用度，单位与 Blender 一致：米、瓦）。
4. **可撤销**：会修改场景的命令一律进入撤销栈。
5. **出错返回结构化信息**：返回 `{ok:false, error:"...", code:"NOT_FOUND"}`，不弹窗阻塞。
6. **可查询**：有“设置”就要有对应的“读取”，让 AI 能确认结果。
7. **双语描述**：工具描述用英文（AI 理解效果最好），界面文字仍走 UiText。

## 5. 线程模型
- D3D11 渲染和 `ModelViewport` 都属于 WPF UI 线程（D3DImage 按需渲染）。管道/HTTP 请求在线程池上收到，**必须**用 `Application.Current.Dispatcher.InvokeAsync(..., DispatcherPriority.Normal)` 调度到 UI 线程执行，再 `await` 结果。
- 导入大模型时，`MeshImporter.Load` 在后台线程运行，GPU 上传放回 UI 线程（和现有 `LoadPaths` 流程一致）。工具调用要等 `SceneChanged` 事件触发后才返回。
- 如果用户正在做模态变换（`IsTransforming == true`），返回 `BUSY`，不打断用户的操作。
- 每个命令设超时（默认 30 秒，导入 120 秒）。命令串行执行，一次只执行一个。
- 截图：在 UI 线程调用 `Invalidate()` 渲染一帧后，从共享纹理 `CopyResource` 到 staging 纹理，`Map` 读取后编码为 PNG（可设置最长边，默认 1024），以 MCP `ImageContent`（base64）返回。

## 6. 撤销集成
- 现在的撤销栈在 `ModelViewport.Transform.cs`（`_undo/_redo`，上限 `UndoLimit=100`），只记录变换。计划改成通用的 `IUndoCommand`（增删物体、灯光、可见性、变换）。
- AI 发出的一次工具调用 = 一个撤销步骤；批量工具（如 `transform_objects`）合并为一步。撤销记录标注 `"AI: move Cube"`，用户可以用 Ctrl+Z 撤回 AI 做的事。
- 提供 `undo` / `redo` 工具。
- AI 修改场景后，标题栏照常显示 `*`（未保存）。

## 7. 安全
- **默认关闭**。在「偏好设置」中新增「AI / MCP」分类：启用开关、显示连接状态、访问令牌（可重新生成）、“需要确认的操作”勾选项、最近调用日志。
- 命名管道只允许当前用户访问（`PipeSecurity` 只给当前 SID 授权）；方案 A 只绑定 `127.0.0.1`，校验 `Origin` 头（防 DNS rebinding），并要求 `Authorization: Bearer <token>`。
- 令牌保存在 `%LOCALAPPDATA%\PolyView\mcp-token`，桥接程序启动时读取。
- **破坏性操作需要确认**（在 PolyView 里弹出深色 `PvMessageDialog`，由用户点同意）：`clear_scene`、`open_project`（会丢弃未保存的修改）、覆盖已有文件的 `save_project_as` / `export`、删除物体（可配置）。
- 文件路径只允许用户设置的工作目录（默认 `C:\Project\C#\PolyView\Export` 和最近导入的目录），拒绝 UNC 路径和系统目录。
- **不提供任意代码/脚本执行。**
- 状态栏显示“AI 已连接”，调用日志写入 `%LOCALAPPDATA%\PolyView\mcp.log`。

## 8. 工具清单（以现有代码为依据）
| 工具 | 说明 | 对应代码 |
|---|---|---|
| `get_scene_info` | 列出物体（id、名称、类型、可见、顶点/三角形数、世界包围盒）、灯光、相机、着色模式、选中项 | `ObjectIds`、`TryGetObjectInfo`、`SceneObjectInfo` |
| `get_object_info(id)` | 详细信息：变换、节点树、源文件 | `TryGetTransform`、`GetNodes` |
| `select(ids[], node?)` | 选择物体/节点；`[]` 表示取消选择 | `Select`、`SelectNode` |
| `set_transform(id, position?, rotation_euler_deg?, scale?)` | 设置绝对值，未提供的字段不变 | `SetTransform`、`ObjectTransform.FromEuler` |
| `transform_objects(ids[], translate?, rotate_deg?, scale?, space)` | 相对变换（与 G/R/S 效果相同） | `ModelViewport.Transform` |
| `set_visibility(id, visible, node?)` | 显示/隐藏 | `SetObjectVisible`、`SetNodeVisible` |
| `delete_object(id)` | 删除（需确认，可配置） | `RemoveObject` |
| `import_model(path, mode=add\|replace)` | 通过 Assimp 等导入 `.obj .stl .glb .gltf .fbx .dae .3ds .ply .json` | `AddPath`、`LoadPaths`、`MeshImporter.SupportedExtensions` |
| `add_light(type, position?, color?, power_w?, ...)` | 类型：Directional/Point/Spot/Ambient，最多 16 个 | `AddLight(SceneLightType)`、docs/07 |
| `set_light(id, ...)` | 修改灯光属性（依赖“灯光属性编辑”功能） | `SceneLight` |
| `set_shading(mode)` | wireframe/solid/material/rendered | `Shading`（`ViewportShading`） |
| `set_view(preset\|yaw,pitch,distance,target, ortho?)` / `frame_selected` / `reset_view` | 控制视角 | `OrbitCamera.SetView/Frame`、`ResetView` |
| `set_active_tool(tool)` | select/move/rotate/scale | `ActiveTool`（`TransformTool`） |
| `get_viewport_screenshot(max_size=1024)` | 返回 PNG 图片，给 AI 视觉理解 | `SceneRenderer`（新增 readback） |
| `undo` / `redo` | 撤销/重做 | `Undo`、`Redo` |
| `save_project(path?)` / `save_project_as(path)` / `open_project(path)` / `new_scene` | 工程文件 | `MainWindow.Project.cs`、`Core/Project/*` |
| `export_model(path, format)` | FBX/OBJ/glTF/GLB/STL/PLY/DAE（等导出功能完成后再加） | 未来的 Assimp 导出 |

## 9. Resources
- `polyview://scene`：完整场景树 JSON（集合 → 物体 → 节点、灯光、相机），内容与 `get_scene_info` 一致。场景变化时发送 `notifications/resources/updated`（订阅 `SceneChanged`/`ObjectChanged`）。
- `polyview://object/{id}`：单个物体的详细信息。
- `polyview://viewport.png`：当前视口截图。
- `polyview://docs/units`：单位与坐标系说明（Z 轴向上、米、瓦，与 Blender 一致）。

## 10. JSON Schema 示例
`set_transform` 的 inputSchema（由 SDK 根据参数自动生成）：
```json
{
  "type": "object",
  "properties": {
    "id": { "type": "integer", "description": "Object id from get_scene_info" },
    "position": { "type": "array", "items": { "type": "number" }, "minItems": 3, "maxItems": 3, "description": "World position in meters [x,y,z], Z up" },
    "rotation_euler_deg": { "type": "array", "items": { "type": "number" }, "minItems": 3, "maxItems": 3 },
    "scale": { "type": "array", "items": { "type": "number" }, "minItems": 3, "maxItems": 3 }
  },
  "required": ["id"]
}
```
`get_scene_info` 的返回示例：
```json
{
  "ok": true,
  "shading": "solid",
  "selected": [3],
  "objects": [
    { "id": 3, "name": "Cube", "kind": "mesh", "visible": true, "vertices": 24, "triangles": 12,
      "position": [0,0,0], "rotation_euler_deg": [0,0,0], "scale": [1,1,1],
      "bounds": { "min": [-1,-1,-1], "max": [1,1,1] } }
  ],
  "lights": [ { "id": 7, "type": "point", "position": [4,1,6], "color": [1,1,1], "power_w": 1000 } ],
  "camera": { "target": [0,0,0], "yaw_deg": 45, "pitch_deg": 30, "distance": 10, "ortho": false }
}
```
C# 工具定义示例：
```csharp
[McpServerToolType]
public sealed class SceneTools(IPolyViewClient pv)
{
    [McpServerTool(Name = "set_transform"), Description("Set absolute transform of an object. Omitted fields are unchanged. Undoable.")]
    public Task<TransformResult> SetTransform(int id, float[]? position = null,
        float[]? rotation_euler_deg = null, float[]? scale = null, CancellationToken ct = default)
        => pv.SendAsync<TransformResult>("set_transform", new { id, position, rotation_euler_deg, scale }, ct);
}
// Program.cs（桥接程序）
var b = Host.CreateApplicationBuilder(args);
b.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace); // stdout 只能输出协议内容
b.Services.AddSingleton<IPolyViewClient, PipeClient>();
b.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
await b.Build().RunAsync();
```
内部管道协议（每行一个 JSON）：`{"id":1,"token":"...","cmd":"set_transform","args":{...}}` → `{"id":1,"ok":true,"result":{...}}`。

## 11. 客户端配置
**Claude Desktop**（`%APPDATA%\Claude\claude_desktop_config.json`）：
```json
{ "mcpServers": { "polyview": {
    "command": "C:\\Project\\C#\\PolyView\\PolyView\\PolyView.Mcp\\bin\\Debug\\net10.0\\PolyView.Mcp.exe" } } }
```
**Cursor**（`.cursor/mcp.json` 或 `~/.cursor/mcp.json`）：
```json
{ "mcpServers": { "polyview": { "command": "C:\\...\\PolyView.Mcp.exe", "args": [] } } }
```
Cursor 也可以直接连方案 A：`{ "mcpServers": { "polyview": { "url": "http://127.0.0.1:47800/mcp", "headers": { "Authorization": "Bearer <token>" } } } }`
**Claude Code**：
```
claude mcp add polyview -- "C:\...\PolyView.Mcp.exe"
claude mcp add --transport http polyview http://127.0.0.1:47800/mcp --header "Authorization: Bearer <token>"   # 方案 A
```
「偏好设置 > AI / MCP」页提供“复制配置”按钮，自动填好 exe 路径。

## 12. 分阶段计划
| 阶段 | 内容 | 验收 |
|---|---|---|
| P0 | 抽出 `AutomationService`（UI 线程调度、ID 定位、结构化错误）；偏好设置中加开关和令牌 | 单元测试直接调用 service |
| P1 | 命名管道服务 + `PolyView.Mcp` stdio 桥接；只读工具：`get_scene_info`、`get_object_info`、`get_viewport_screenshot`；资源 `polyview://scene` | MCP Inspector 能列出工具并看到截图 |
| P2 | 修改类工具：选择、变换、可见性、着色、视角、工具切换、加灯；通用撤销栈 | Claude 能完成“把立方体抬高 2 米并加一盏点光” |
| P3 | 文件类工具：导入、保存/另存为/打开/新建 + 确认弹窗 + 路径白名单 | 破坏性操作没有用户确认就不会执行 |
| P4 | 导出工具（等 Assimp 导出完成）；资源订阅通知；可选的方案 A（HTTP） | 导出后重新导入，结果一致 |

## 13. 测试
- **单元**：`AutomationService` 每个命令都测正常路径、`NOT_FOUND`、参数越界、撤销/重做（沿用 `ProjectSelfTest` 的自测方式）。
- **协议**：用 `npx @modelcontextprotocol/inspector <PolyView.Mcp.exe>` 手动检查；再用官方 SDK 的 `McpClient` 写集成测试：启动 PolyView（`--automation-test`）→ 调用工具 → 用 `get_scene_info` 检查结果。
- **线程**：并发发送 50 个请求，确认按顺序执行、没有跨线程异常；导入时同时发送截图请求。
- **安全**：错误令牌、其他用户访问管道、白名单外路径、未确认的破坏性操作，都必须被拒绝。
- **端到端**：在 Claude Desktop 里用自然语言完成 P2 的验收任务，并截图存档。
