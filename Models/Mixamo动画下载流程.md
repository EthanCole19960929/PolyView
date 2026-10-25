# Mixamo 角色动画批量下载流程（交接文档）

> 目标：给一个已上传到 Mixamo 的角色，批量下载各类动作（FBX），按类别分文件夹放进该角色的 `Animations` 目录。
> 已完成示例：`boy`（约 87 个）；`girl` 按同样方法进行中/已完成。

## 1. 目录结构（必须保持一致）

```
C:\Project\C#\PolyView\Models\
├─ <角色名>\                 例如 boy、girl
│  ├─ <角色名>.blend / .fbx / .pvproj / 预览图   （已有，不要动）
│  └─ Animations\
│     ├─ 01_Idle            待机
│     ├─ 02_Walk            走路
│     ├─ 03_Run             跑步
│     ├─ 04_Jump            跳跃
│     ├─ 05_Attack_Melee    近战攻击、挥砍、拳击
│     ├─ 06_Attack_Kick     踢腿
│     ├─ 07_Dodge_Roll      闪避、翻滚
│     ├─ 08_Hit_Reaction    受击
│     ├─ 09_Death           死亡
│     ├─ 10_Misc            其他（捡东西、爬梯、游泳、起身、格挡、胜利）
│     └─ README.txt         分类与导出设置说明
```

- 只分一层，不要再嵌套子文件夹（用户偏好浅层目录）。
- 文件名保留 Mixamo 的动画标题，例如 `Breathing Idle.fbx`；标题有歧义或会重名时，加上描述，如 `Mma Kick - Roundhouse.fbx`。
- 路径里含有 `#`，PowerShell 里路径一律用单引号或 `-LiteralPath`。

## 2. 前置条件

1. 用户已在 BrowserOS neo（用户电脑上的浏览器）里登录 Adobe / Mixamo。
2. 角色模型已经在 Mixamo 上传并自动绑定骨骼。Mixamo 右侧预览区标题类似 `XXX ON GIRL_60K...`，说明当前选中的是该角色。如果不是，去 Characters 页面选中用户上传的那个模型；找不到就停下来问用户，不要换别的角色下载。
3. 不要提取 Cookie 或令牌，只像真人一样操作页面。
4. 不要关闭用户自己的标签页，只关闭自己开的。

## 3. 操作浏览器的方法（BrowserOS neo MCP）

BrowserOS neo 在本机提供 MCP 服务，地址是 `http://127.0.0.1:9210/mcp`（备用 `http://127.0.0.1:9010/mcp`），协议是 Streamable HTTP。

要点：

- 先 `initialize`（protocolVersion `2025-03-26`），从响应头取 `mcp-session-id`，之后每个请求都带上这个头；再发 `notifications/initialized`；然后 `tools/list` 看有哪些工具。
- 请求头：`Content-Type: application/json` 和 `Accept: application/json, text/event-stream`。响应是 SSE（以 `data:` 开头的行）。
- PowerShell 下直接写 JSON 会因为引号被破坏。正确做法：把 JSON 写进临时文件，再用 `curl.exe --data-binary "@文件"` 发送。
- 工具大致有：`run`（执行一段 JS 脚本，优先用它）、`tabs`、`snapshot`、`act`、`read`、`download`、`evaluate`、`name_session` 等。会话开始先用 `name_session` 起个名字；第一次先 `tabs action=list`，再开自己的标签页。
- 遇到登录、验证码等只能人做的步骤，用 `request_human_help`，并告诉用户。

## 4. 下载单个动画的步骤

1. 打开 `https://www.mixamo.com`，进 Animations 页面，在搜索框输入关键词（英文），例如 `punch`、`kick`、`jump`、`death`。
2. 在结果网格里点选一个动画，右侧会用当前角色试播，确认标题里的角色名正确。
3. 如果是走路、跑步类，右侧面板有 `In Place`（原地）勾选项时勾上；其他类型不勾，保留位移。
4. 点右上角 `Download`，对话框里设置：
   - Format：`FBX Binary(.fbx)`
   - Skin：`With Skin`（所有文件都带皮肤，每个约 5 MB 以上；不要下载 Without Skin）
   - Frames per second：`30`
   - Keyframe Reduction：`none`
5. 点 Download，文件落到浏览器的下载目录（一般是 `C:\Users\<用户名>\Downloads`，以 `download` 工具返回的路径为准）。
6. 每个动画之间留几秒间隔，避免触发限流。

## 5. 整理与校验

- 把下载的文件移到对应类别文件夹，保留标题作为文件名。
- 校验每个文件：大小不为 0；文件开头是 `Kaydara FBX Binary`；对所有文件算 MD5，确认没有重复。
- 清理：下载目录里不要留残余，删除临时 JSON 文件。
- 在 `Animations` 下写 `README.txt`（UTF-8），列出类别和导出设置。
- 不删除用户已有文件。

## 6. 推荐动作清单（每类 2~3 个，种类多比数量多重要）

| 类别 | 搜索关键词和常用动作 |
|---|---|
| 01_Idle | Breathing Idle、Fighting Idle、Crouch Idle、Unarmed Idle |
| 02_Walk | Standard Walk、Walking Backwards、Walk Strafe（左右） |
| 03_Run | Standard Run、Unarmed Run Forward/Back、Standing Run Left/Right |
| 04_Jump | Jumping Up、Standing Jump、Running Jump |
| 05_Attack_Melee | Sword And Shield Slash/Power Slash、Heavy Weapon Swing、Punch Combo、Hook Punch |
| 06_Attack_Kick | Mma Kick（Roundhouse）、Kicking（Front Snap Kick）、Side Kick |
| 07_Dodge_Roll | Dodging、Sprinting Forward Roll、Quick Roll To Run、Stand To Roll |
| 08_Hit_Reaction | Hit Reaction、Standing React Large From Front/Back、Head Hit |
| 09_Death | Death、Falling Back Death、Standing React Death Forward |
| 10_Misc | Picking Up、Climbing Ladder、Swimming、Getting Up、Standing Block Idle、Victory Idle |

跳过：只有拿弓、枪的动作，怪物或僵尸、跳舞、搞怪类动作（除非用户另有要求）。

## 7. 在 Unity 中使用

1. 每个动画 FBX 都带皮肤（With Skin，每个约 5 MB 以上），可以单独导入：Rig 设为 `Humanoid`（或 `Generic`），各自生成 Avatar。
2. 如果想让所有动画共用一个 Avatar：先选一个 FBX 生成 Humanoid Avatar，其余 FBX 的 Rig 设为 `Humanoid`，Avatar 选 `Copy From Other Avatar` 指向它。
3. 原地版的走路和跑步用代码或 Animator 控制位移；带位移的翻滚、闪避可以勾选 Root Motion。

## 8. 完成后汇报

- 每个文件夹的文件数量和文件名清单；
- 失败或被跳过的项目；
- 下载时 Mixamo 里显示的角色名；
- 使用的设置（Skin、In Place、fps）。