# 12 PolyView 与 Blender 联动设计

> 状态：设计草案，等用户确认后再实现。本文档只是方案，还没写代码。

## 1. 目标

1. **一键发送到 Blender**：在 PolyView 里点一下，就把当前场景或选中的物体在 Blender 里打开。
2. **导入 Blender 文件**：PolyView 能直接打开 `.blend` 文件和 Blender 导出的模型。
3. **双向实时编辑同步（Live Link）**：两边同时开着，一边改了位置、可见性、灯光、增删物体或网格，另一边会自动跟着变。
4. **AI 联动**：复用 MCP 命令层（见 `11_MCP接入设计.md`），让 AI 能同时操作 PolyView 和 Blender。

不做的事：不重写 Blender 的建模功能，也不在 PolyView 里自己解析 `.blend` 二进制格式（这个格式没有公开规范，跟着 Blender 版本变）。

## 2. 现有项目调研

| 项目 | 做法 | 能借鉴的地方 |
|---|---|---|
| [ahujasid/blender-mcp](https://github.com/ahujasid/blender-mcp) | Blender 插件 `addon.py` 开一个本机 TCP 服务（默认 9876 端口），收到 JSON 命令 `{type, params}` 后用 `bpy.app.timers` 在主线程执行；另有一个 Python MCP 服务转发 AI 的请求 | 插件结构、JSON 命令格式、主线程调度，直接照用 |
| [KhronosGroup/glTF-Blender-IO](https://github.com/KhronosGroup/glTF-Blender-IO) | Blender 自带的 glTF 导入导出 | 第 1、2 阶段的文件交换格式，接口是 `bpy.ops.import_scene.gltf` / `export_scene.gltf` |
| Unreal Live Link / Unity Live Link（Maya、MotionBuilder 等 DCC 实时推送变换给引擎） | DCC 端插件推送，引擎端订阅，按“主体名”对应 | 物体 ID 对应、只推送变化的部分、限频 |
| NVIDIA Omniverse Blender Connector（基于 USD 增量同步，闭源） | 以 USD 为中间格式，多软件同时编辑 | 冲突处理和增量同步的思路 |
| [assimp/assimp](https://github.com/assimp/assimp) | PolyView 已在用 | 读取 Blender 导出的 glTF/FBX |

结论：第 3 阶段的插件就按 blender-mcp 的结构写（本机 socket + JSON + `bpy.app.timers`），文件交换统一用 glTF（GLB）。

## 3. 阶段一：一键发送到 Blender

**流程**
1. 用户点「文件 > 发送到 Blender」（或在大纲里右键选中的物体）。
2. PolyView 调用导出功能（正在开发的 `Core/Export`），把场景导出为 `%TEMP%\PolyView\send_<时间>.glb`。默认 glTF，可在偏好设置改成 FBX。
3. 启动 Blender：
   ```
   blender.exe --python-expr "import bpy; bpy.ops.wm.read_homefile(use_empty=True); bpy.ops.import_scene.gltf(filepath=r'...glb')"
   ```
   或者用 `--python polyview_import.py -- <文件路径>`，脚本放在 PolyView 安装目录，更方便加参数（单位、是否清空场景、是否保存成 .blend）。
4. 如果 Blender 已经开着并装了联动插件（阶段三），就不再新开窗口，直接通过 socket 让已开的 Blender 导入。

**找到 Blender 的位置**（按顺序找，找到就停）：
1. 偏好设置里用户手动选的路径（优先级最高）。
2. 注册表：`HKCR\blendfile\shell\open\command`，以及 `HKLM\SOFTWARE\BlenderFoundation` 下的安装路径。
3. `C:\Program Files\Blender Foundation\Blender *\blender.exe`，有多个版本时选最新的。
4. Steam：读 `libraryfolders.vdf`，找 `steamapps\common\Blender\blender.exe`。
5. Microsoft Store 版、PATH 里的 `blender`。
找到后运行 `blender --version` 记下版本号，在偏好设置里显示出来。最低支持 Blender 4.2。

**要注意**
- 坐标：glTF 是 Y 轴朝上，Blender 导入时会自动转回 Z 轴朝上，PolyView 导出时要设置正确，确保往返后位置一样。
- 单位：两边都用米。
- 灯光：glTF 用 `KHR_lights_punctual`，Blender 导入时选 “Standard” 光照单位模式，功率才对得上（参考 `07_灯光`）。

## 4. 阶段二：导入 Blender 文件

**流程**
1. 用户打开或拖入 `.blend` 文件。
2. PolyView 在后台运行：
   ```
   blender.exe -b "file.blend" --python polyview_export_gltf.py -- "%TEMP%\PolyView\from_blend.glb"
   ```
   脚本里调用 `bpy.ops.export_scene.gltf(filepath=..., export_format='GLB', use_visible=False, export_apply=True, export_lights=True, export_cameras=True)`。
3. 脚本另外写一个 `meta.json`：集合结构、隐藏状态、物体名，补充 glTF 记不下的信息。
4. PolyView 用 Assimp 读 GLB，再按 `meta.json` 还原集合和可见性。
5. 界面显示进度条，可以取消；没找到 Blender 就提示用户在偏好设置里选路径。

**性能**：Blender 后台启动约 1～3 秒，大文件导出更久。按“文件路径 + 修改时间”做缓存，没改过就直接读缓存。

## 5. 阶段三：双向实时联动（Live Link）

### 5.1 架构

```
PolyView (C#)  ⇄  localhost TCP / WebSocket  ⇄  Blender 插件 polyview_link (Python/bpy)
      ↑                                              ↑
   MCP 命令层  ← AI                               bpy 主线程（bpy.app.timers）
```

- Blender 插件当服务端，监听 `127.0.0.1:9877`（不和 blender-mcp 的 9876 冲突，端口可改）。PolyView 当客户端，在 MCP 面板 / 偏好设置里点「连接 Blender」。
- 协议：一行一个 JSON（或 WebSocket 文本帧），格式和 MCP 命令层一致：
  ```json
  {"v":1, "seq":42, "src":"polyview", "type":"transform", "id":"pv-3f2a...", "loc":[0,0,1], "rot_quat":[1,0,0,0], "scale":[1,1,1]}
  ```
- 网格数据较大，用二进制帧（WebSocket binary 或长度前缀），顶点用 float32。

### 5.2 物体 ID 对应

- 每个物体有一个固定的 `polyview_id`（GUID）。PolyView 存在 `.pvproj` 里，Blender 存成自定义属性 `obj["polyview_id"]`，随 `.blend` 一起保存。
- 第一次连接时做“握手同步”：双方交换 `{id, 名字, 类型, 变换, 网格哈希}` 列表；对得上的按 ID 对应，没有 ID 的在对方那边新建并分配 ID。
- 物体改名不影响对应关系。

### 5.3 同步的内容

| 消息 | 内容 | 频率 |
|---|---|---|
| `transform` | 位置、旋转（四元数）、缩放 | 拖动时最多 30 次/秒，松手时补发一次最终值 |
| `visibility` | 显示/隐藏、集合开关 | 立即 |
| `light` | 类型、颜色、功率、半径、角度 | 立即（拖滑块时限频） |
| `add` / `delete` | 新建/删除物体，新建时附带完整数据 | 立即 |
| `mesh` | 网格变化：先发哈希，对方没有才发数据；只改了顶点位置就只发位置数组 | Blender 退出编辑模式时发；编辑模式中最多 5 次/秒 |
| `selection` | 当前选中的物体和活动物体 | 可选，默认关闭 |
| `hierarchy` | 父子关系、集合 | 立即 |

**Blender 端怎么知道改了什么**：用 `bpy.app.handlers.depsgraph_update_post`，看 `depsgraph.updates` 里哪些物体的 `is_updated_transform` / `is_updated_geometry` 为真，只发这些。

**PolyView 端**：在现有的修改入口（变换、灯光、增删、可见性，也就是撤销系统和 MCP 用的同一套命令）加一个钩子，命令执行后发消息。

### 5.4 防止来回反弹和冲突

- 每条消息带 `src` 和 `seq`。收到对方的修改后应用时打上“远程修改”标记，不再发回去，避免死循环。
- 冲突规则：**以最后修改为准**（按到达顺序）。同一个物体两边同时在拖，以正在拖的一方为准，另一方的消息先丢掉，松手后再对齐。
- 断线重连后重新握手同步，有差异时弹窗让用户选“以 PolyView 为准”还是“以 Blender 为准”。
- 远程修改在 PolyView 里也进撤销历史，可以撤销。

### 5.5 坐标和单位

- 两边都是 Z 轴朝上、右手坐标系、单位米，变换可以直接对应，不需要转换。
- 旋转统一用四元数传，避免欧拉角顺序不同的问题（Blender 默认 XYZ 欧拉）。
- 灯光功率统一用 Blender 的单位（W），和 `07_灯光` 一致。
- 如果 Blender 场景的 `unit_settings.scale_length` 不是 1，握手时告诉 PolyView 进行换算。

### 5.6 线程

- **Blender**：bpy 只能在主线程用。socket 收发在后台线程，收到的消息放进 `queue.Queue`，用 `bpy.app.timers.register(处理函数, persistent=True)` 每 0.02 秒在主线程取出执行（blender-mcp 就是这么做的）。
- **PolyView**：网络收发用 `async`，收到后用 `Dispatcher.InvokeAsync` 回到 UI 线程修改场景，然后请求重绘（按需渲染）。

### 5.7 性能目标

| 项 | 目标 |
|---|---|
| 拖动时对方延迟 | < 50 ms（本机） |
| 单次变换消息 | < 200 字节 |
| 10 万顶点网格同步 | < 300 ms |
| 空闲时 CPU | 接近 0，没有变化不发消息，只有每 5 秒一次心跳 |
| 帧率影响 | PolyView 拖动不低于 60 fps |

## 6. 阶段四：和 MCP 共用一套命令

- Live Link 的消息类型和 MCP 工具（`scene_list`、`set_transform`、`add_light` 等）用同一套命令定义，一份 schema 两边用。
- PolyView 的 MCP 服务新增几个工具：`blender_status`、`send_to_blender`、`import_blend`、`blender_exec`（在 Blender 里执行操作，比如加细分修改器）。
- 这样 AI 只要连 PolyView 一个 MCP，就能同时操作两个软件，例如：“把选中的模型发到 Blender，加两级细分，再同步回来”。
- `blender_exec` 能跑任意 Python，风险高，默认关闭，需要在偏好设置里手动打开，每次执行都记日志。

## 7. 插件安装

- 插件 `polyview_link` 放在 PolyView 安装目录的 `Integrations/Blender/` 下，是一个标准的 Blender 4.2 扩展（`blender_manifest.toml`）。
- 偏好设置里点「安装 Blender 插件」：
  1. 用找到的 Blender 运行 `blender -b --python-expr "import bpy; bpy.ops.preferences.addon_install(filepath=...); bpy.ops.preferences.addon_enable(module='polyview_link'); bpy.ops.wm.save_userpref()"`；
  2. 或者直接复制到 `%APPDATA%\Blender Foundation\Blender\<版本>\scripts\addons\`（4.2 扩展则放到 `extensions\user_default\`）。
- 显示插件版本，PolyView 升级后提示更新插件。协议带版本号 `v`，版本不一致时提示用户。

## 8. 安全

- 只监听 `127.0.0.1`，不对外网开放。
- 连接时要带令牌：插件第一次启动时生成随机令牌写到 `%APPDATA%\PolyView\blender_link.token`，PolyView 读这个文件；令牌不对就断开。
- 默认不允许执行任意 Python（见第 6 节）。
- 发送到 Blender 的临时文件放在 `%TEMP%\PolyView\`，退出时清理。

## 9. 测试计划

1. **往返测试**：PolyView → glTF → Blender → glTF → PolyView，比较位置、旋转、缩放、层级、法线、UV、灯光，误差 < 1e-4。
2. **找 Blender 测试**：Program Files、Steam、手动路径、没装 Blender 四种情况。
3. **`.blend` 导入**：Blender 自带的示例文件、有集合、有隐藏物体、有修改器、大文件（100 万面）。
4. **Live Link**：两边拖同一个物体、断线重连、快速增删 100 个物体、编辑模式改网格，检查没有来回反弹和内存增长。
5. **自动化**：用 `blender -b` 跑插件的单元测试；PolyView 端用假的服务端模拟消息。
6. **性能**：测第 5.7 节里的各项数字。

## 10. 风险

| 风险 | 应对 |
|---|---|
| Blender 版本更新改了 API | 锁定支持 4.2 LTS 及以后，插件里做版本判断 |
| glTF 记不下所有信息（修改器、复杂材质、集合） | 修改器导出时应用；集合等补充信息放 `meta.json` |
| 实时同步大网格卡顿 | 限频、哈希去重、只发变化的部分，编辑模式中降低频率 |
| 两边同时改导致结果不一致 | 最后修改为准，重连时让用户选择 |
| 用户没装 Blender | 功能入口变灰，提示下载或选择路径 |
| `blender_exec` 被滥用 | 默认关闭、令牌、日志 |

## 11. 工作量估计

| 阶段 | 内容 | 估计 |
|---|---|---|
| 一 | 发送到 Blender、找 Blender 路径、偏好设置项 | 1～2 天（导出功能完成后） |
| 二 | 导入 `.blend`、导出脚本、缓存、进度条 | 2～3 天 |
| 三 | 插件 + 协议 + 变换/可见性/灯光/增删同步 | 5～7 天 |
| 三+ | 网格增量同步、冲突处理、重连 | 4～6 天 |
| 四 | MCP 工具接入 | 1～2 天 |
| | 测试和修 bug | 3～5 天 |

## 12. 待确认的问题

1. 用户说的“变机”是否就是“编辑”（两边都能改）？
2. 发送到 Blender 默认用 glTF 还是 FBX？（建议 glTF，往返更准确）
3. 实时同步是否也要同步材质和贴图？（建议第一版不做）
4. 是否需要支持多个 Blender 同时连一个 PolyView？
5. 阶段三是自己写插件，还是基于 blender-mcp 扩展？（建议自己写，结构照它）
6. 最低支持的 Blender 版本是否定为 4.2 LTS？
