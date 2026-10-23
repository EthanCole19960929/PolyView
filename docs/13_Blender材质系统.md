# 13 材质系统规格（Blender 4.2 参考 + PolyView 借鉴方案）

> 负责人：program（只有 program 写代码和提交）。本文由 novel 编写，只是文档。
> 参考：Blender 4.2 LTS 手册 `render/materials/assignment`、`render/shader_nodes/shader/principled`、`render/eevee/material_settings`。灯光部分见 `07_灯光规格.md`。

---

# 第一部分：Blender 4.2 材质系统

## 1. 基本概念

| 概念 | 说明 |
|---|---|
| 材质数据块（Material data-block） | 材质是独立的数据块，可以新建，也可以被多个物体共用。改一次，所有用它的物体一起变。 |
| 材质槽（Material Slot） | 物体和材质之间的“插座”。默认只有 1 个槽，整个物体都用这个材质。要让不同的面用不同材质，就加多个槽，每个面记录自己用第几个槽（material index）。 |
| 关联方式（Link：Data / Object） | 每个槽可以选材质挂在 **Data（网格数据）** 上还是 **Object（物体）** 上。挂在 Data 上：共用同一网格的物体也共用这个材质。挂在 Object 上：共用网格的多个物体可以有各自的材质。 |
| 用户数（Users）/ 伪用户（Fake User） | 材质名旁的数字表示有几处在用它。用户数为 0 且没有 Fake User（盾牌图标）的材质，保存文件后重新打开就没了。点亮 Fake User 可以让没人用的材质也一直保留。 |
| 节点树（Node Tree） | 材质的内容是一组着色器节点，最后连到 **Material Output**。默认新建材质是 **Principled BSDF → Material Output (Surface)**。 |

## 2. 在哪里添加、指定、删除材质

### 2.1 属性编辑器 › 材质（Material）标签页
属性面板（右侧）最下面的红色球形图标。上半部分是 **Material Slots** 面板：

- **槽列表**：列出物体的所有材质槽，高亮的是“活动槽”。
- **`+` Add Material Slot**：新增一个空槽。
- **`-` Remove Material Slot**：删除当前槽（材质数据块本身不会被删，只是这个物体不再用它）。
- **`v` 下拉菜单（Material Specials）**：
  - **Copy Material**：把当前材质的节点和设置复制到剪贴板；
  - **Paste Material**：把剪贴板里的节点和设置粘贴到当前材质；
  - **Copy Material to Selected**：把活动物体的材质分配复制给其他选中的物体；
  - **Remove Unused Slots**：删除所有没有面在用的槽。
- **上移/下移箭头**：调整槽的顺序。

槽列表下面是数据块行：
- **浏览按钮（球形图标）**：弹出当前文件里所有材质，可搜索（如输入 wood 过滤），点一个就指定到当前槽。
- **材质名输入框**：双击改名。
- **用户数字**：点它可以复制出一份单独的材质（Make Single User）。
- **Fake User（盾牌）**。
- **New**：槽为空时显示，新建一个材质。
- **新建副本图标**：复制当前材质为新材质。
- **`X` Unlink**：把材质从这个槽断开。
- **Link 下拉**：Data / Object。

### 2.2 编辑模式下的指定（不同面用不同材质）
在编辑模式下槽列表下面多出三个按钮：
- **Assign**：把选中的面指定给活动槽；
- **Select**：选中所有用活动槽的面；
- **Deselect**：取消选中这些面。

手册给的流程：
1. 物体模式下先建一个基础材质；
2. `Tab` 进编辑模式，切到面选择；
3. 选中要换材质的面；
4. 点 `+` 新建槽，新建或浏览一个材质；
5. 点 **Assign**。

### 2.3 3D 视口
- **Ctrl+L › Link Materials**：先选目标物体，最后选带材质的物体（活动物体），按 Ctrl+L，所有选中物体都改用活动物体的材质。

### 2.4 Shading 工作区
顶部 **Shading** 工作区：上方是“材质预览”模式的视口，下方是 **Shader Editor（着色器编辑器）**，编辑器顶栏也有同样的材质槽下拉、New、浏览按钮。`Shift+A` 添加节点。

### 2.5 删除材质
- 在浏览列表里选中材质点 `X`，或者在槽上 Unlink。
- 如果其他物体还在用它，只是从当前物体上移除，其他物体保持不变。
- 用户数为 0 且没有 Fake User 时，保存并重新打开文件后材质才真正消失。
- 立即清理：**File › Clean Up › Purge Unused Data**（4.2 里叫这个，旧版本叫 Orphan Data 的 Purge），或者在大纲视图切到 **Orphan Data / Unused Data** 显示后点 Purge。

## 3. Principled BSDF 全部输入（4.2）

基于 OpenPBR，分层结构：底层（金属/漫反射/次表面/透射）→ 清漆层（Coat）→ 光泽层（Sheen），自发光在 Coat 和 Sheen 之下。

| 分组 | 输入 | 范围 / 默认 | 作用 |
|---|---|---|---|
| 基础 | Base Color | 颜色 | 整体颜色，用于漫反射、次表面、金属和透射 |
| | Metallic | 0–1 | 0 = 电介质（塑料、木头），1 = 金属，反射带底色 |
| | Roughness | 0–1（默认 0.5） | 0 镜面清晰，1 完全模糊 |
| | IOR | 1.0–4.0（默认 1.5） | 折射率，影响反射强度和折射 |
| | Alpha | 0–1 | 透明度，1 不透明，常接贴图的 Alpha |
| | Normal | 向量 | 底层法线，接 Normal Map |
| Subsurface 次表面 | Method | Christensen-Burley / Random Walk / Random Walk (Skin) | 后两个只在 Cycles |
| | Weight | 0–1 | 漫反射和次表面的混合 |
| | Radius | RGB | 光在表面下散射的平均距离 |
| | Scale | 距离 | 乘到 Radius 上 |
| | IOR、Anisotropy | | 只在 Cycles |
| Specular 高光 | Distribution | GGX / Multiscatter GGX | 微表面分布 |
| | IOR Level | 0–1（0.5 不变） | 调高光强度，0 无反射，1 翻倍 |
| | Tint | 颜色 | 高光色调；金属时调边缘色 |
| | Anisotropic、Anisotropic Rotation | | 各向异性，只在 Cycles |
| | Tangent | 向量 | 各向异性方向 |
| Transmission 透射 | Weight | 0–1 | 玻璃、液体 |
| Coat 清漆 | Weight、Roughness、IOR、Tint、Normal | | 车漆、清漆层 |
| Sheen 光泽 | Weight、Roughness、Tint | | 布料绒毛、灰尘 |
| Emission 自发光 | Color、Strength | Strength 默认 0 | Strength=1 时颜色和 Emission Color 一样（相当于无光照） |
| Thin Film 薄膜 | Thickness（nm，0 关闭）、IOR（默认 1.33） | | 油膜、肥皂泡，只在 Cycles |

输出：**BSDF**。

## 4. 其他常用节点（Shift+A 添加）

| 节点 | 分类 | 用途 |
|---|---|---|
| Image Texture | Texture | 加载图片贴图，输出 Color 和 Alpha；可选插值、投影、扩展方式，以及色彩空间（颜色图用 sRGB，法线、粗糙度图用 Non-Color） |
| Texture Coordinate | Input | 提供 UV、Generated、Object 等坐标 |
| UV Map | Input | 选用哪一套 UV |
| Mapping | Vector | 对坐标做平移、旋转、缩放 |
| Normal Map | Vector | 把法线贴图转成法线，接到 BSDF 的 Normal；有 Strength |
| Bump | Vector | 用灰度图做凹凸 |
| Mix（Color 模式） | Utility | 按 Factor 混合两种颜色，有 Multiply、Overlay 等混合模式 |
| Color Ramp | Converter | 把灰度映射成渐变色 |
| Emission | Shader | 纯自发光着色器 |
| Transparent BSDF | Shader | 完全透明 |
| Mix Shader | Shader | 按 Fac 混合两个着色器，如 Principled 和 Transparent |
| Material Output | Output | 终点：Surface、Volume、Displacement、Thickness |

## 5. 材质属性里的其他面板

### 5.1 Settings（EEVEE，4.2 新版 EEVEE）
- **Pass Index**：材质索引渲染通道用。
- **Surface**：
  - **Backface Culling**：Camera / Shadow / Light Probe Volume 三个开关，隐藏背面；
  - **Displacement**：Bump Only / Displacement Only（会退回到 Displacement and Bump）/ Displacement and Bump，加 Max Distance；
  - **Transparent Shadows**：透明阴影；
  - **Render Method**：**Dithered**（抖动透明，延迟渲染，兼容光追；有 Raytraced Transmission 选项）/ **Blended**（真正颜色混合，前向渲染；只按物体原点排序；有 **Transparency Overlap** 选项）；
  - **Thickness**：Sphere / Slab，From Shadow。
- **Volume › Intersection**：Fast / Accurate。

> 4.2 去掉了旧版的 Blend Mode / Shadow Mode，改成 Render Method。

### 5.2 Viewport Display（视口显示）
材质在 **Solid** 模式下用的颜色和参数：**Color**、**Metallic**、**Roughness**。它和节点树无关，只影响实体模式。

### 5.3 Preview（预览）
面板顶部的预览球，可以切换球体、立方体、毛发、世界等形状看效果。

## 6. 视口着色模式（视口右上角四个球，或 Z 键饼菜单）
| 模式 | 效果 |
|---|---|
| Wireframe | 线框 |
| Solid | 实体，不跑节点；颜色可以选 Material（用 Viewport Display 颜色）、Object、Attribute、Single、Random、Texture |
| Material Preview | 用 EEVEE 和内置 HDRI 显示真实材质，不用场景灯光 |
| Rendered | 用当前渲染引擎和场景灯光 |

## 7. glTF 导入/导出时 Principled BSDF 的对应关系

| glTF | Blender |
|---|---|
| baseColorFactor / baseColorTexture | Base Color（贴图接 Image Texture，Alpha 接 Alpha） |
| metallicFactor / roughnessFactor | Metallic / Roughness |
| metallicRoughnessTexture（B=金属，G=粗糙） | Separate Color 后分别接 Metallic、Roughness |
| normalTexture + scale | Normal Map 节点 → Normal |
| occlusionTexture（R） | 导出用 glTF Material Output 分组节点保存 |
| emissiveFactor / emissiveTexture / KHR_materials_emissive_strength | Emission Color / Strength |
| alphaMode OPAQUE / MASK / BLEND, alphaCutoff | Alpha 输入 + Render Method（MASK 用节点做阈值） |
| doubleSided | Backface Culling 关闭 |
| KHR_materials_transmission / ior / clearcoat / sheen / specular | Transmission / IOR / Coat / Sheen / Specular |

---

# 第二部分：PolyView 借鉴方案

目标：先做到“能正确显示 glTF 的 PBR 材质 + 像 Blender 一样在属性面板里改材质”，不做节点编辑器。

## 1. 数据模型

```csharp
public enum AlphaMode { Opaque, Mask, Blend }

public sealed class Material            // 相当于 Blender 的材质数据块
{
    public Guid Id;
    public string Name = "Material";
    public Vector4 BaseColor = new(0.8f, 0.8f, 0.8f, 1f);   // 线性空间
    public TextureRef? BaseColorTexture;                    // sRGB
    public float Metallic = 0f;
    public float Roughness = 0.5f;
    public TextureRef? MetallicRoughnessTexture;           // G=粗糙 B=金属，线性
    public TextureRef? NormalTexture; public float NormalScale = 1f;
    public TextureRef? OcclusionTexture; public float OcclusionStrength = 1f;
    public Vector3 EmissiveColor = Vector3.Zero; public float EmissiveStrength = 1f;
    public TextureRef? EmissiveTexture;                    // sRGB
    public AlphaMode AlphaMode = AlphaMode.Opaque;
    public float AlphaCutoff = 0.5f;
    public bool DoubleSided = false;                       // = Blender 关闭 Backface Culling
    public bool FakeUser;                                  // 没人用也保留
    public int Version;                                    // 改动后 +1，让 GPU 缓存重建
}

public sealed class TextureRef { public string Key; public int TexCoord; /* 以后加 UV 变换 */ }

public sealed class MaterialSlot { public Material? Material; }

// 网格：按子网格（glTF primitive / Assimp mesh）分段
public sealed class SubMesh { public int IndexStart, IndexCount; public int MaterialIndex; }
// 场景物体：List<MaterialSlot> Slots；SubMesh.MaterialIndex 指向这里
public sealed class MaterialLibrary { public List<Material> Materials; Material Default; int UserCount(Material m); }
```

- 槽挂在物体上（相当于 Blender 的 Link=Object），P0 不做 Link=Data。
- 新建立方体：1 个槽，材质为空 → 用默认灰材质（0.8 灰，Roughness 0.5）。
- 贴图按路径或哈希去重缓存，多个材质共用同一张图。

## 2. 导入映射

**SharpGLTF（.gltf/.glb）**：
- `ModelRoot.LogicalMaterials` → `Material`；`material.FindChannel("BaseColor")` 取 `Color` 和 `Texture`，`"MetallicRoughness"` 取 `Parameters` 里的金属和粗糙，`"Normal"`、`"Occlusion"`、`"Emissive"` 同理；`material.Alpha`、`AlphaCutoff`、`DoubleSided`。
- 每个 `MeshPrimitive` 生成一个 SubMesh，`primitive.Material` 决定 MaterialIndex。
- 贴图取 `Texture.PrimaryImage.Content`（内嵌或外部），用 WIC 解码。
- 读顶点的 `TEXCOORD_0`、`TANGENT`（没有时用 MikkTSpace 或简单方法算切线）。

**Silk.NET.Assimp（.fbx/.obj 等）**：
- `scene->MMaterials[i]`：`$clr.diffuse` 或 `AI_MATKEY_BASE_COLOR` → BaseColor；`AI_MATKEY_METALLIC_FACTOR` / `ROUGHNESS_FACTOR`（没有时用 shininess 估算 Roughness = sqrt(2/(shininess+2))）；`$clr.emissive`；`$mat.opacity` < 1 → Blend；贴图 `TextureType.Diffuse/BaseColor`、`Normals`、`Emissive`、`Metalness`、`DiffuseRoughness`。
- `mesh->MMaterialIndex` → SubMesh.MaterialIndex。
- 导入选项加 `CalculateTangentSpace`、`FlipUVs`（按 D3D 惯例确认）。
- 路径以 `*0` 开头的是内嵌贴图，从 `scene->MTextures` 取。

## 3. 渲染

- **着色器**：把现在的简单光照换成 PBR **Cook-Torrance**：GGX 法线分布 + Smith-GGX 遮蔽 + Schlick 菲涅尔，F0 = lerp(0.04, BaseColor, Metallic)，漫反射 = BaseColor·(1−Metallic)/π。沿用 `07_灯光规格.md` 的 `cbuffer b1`（最多 16 盏灯），每盏灯算一次 BRDF。
- **材质常量**：新增 `cbuffer b2 MaterialCB { float4 BaseColor; float3 Emissive; float Metallic; float Roughness; float NormalScale; float AlphaCutoff; float OcclusionStrength; uint Flags; /*有哪些贴图、AlphaMode*/ }`，16 字节对齐。
- **贴图槽**：t0 BaseColor（`R8G8B8A8_UNORM_SRGB`），t1 MetallicRoughness，t2 Normal，t3 Occlusion，t4 Emissive（SRGB）；s0 线性 Wrap + 各向异性采样器。没有贴图时绑 1×1 白图（法线绑 (0.5,0.5,1)），着色器不用分支。生成 mipmap。
- **环境光**：P0 用简单的半球环境光（天空色 / 地面色），P1 再加 HDRI / IBL（给 Material Preview 用）。
- **绘制顺序**：
  1. Opaque 和 Mask（Mask 在像素着色器里 `clip(alpha - cutoff)`），按材质排序减少状态切换；
  2. Blend：关闭深度写入、开启 alpha 混合，按物体中心到相机的距离从远到近排序（和 Blender 一样按物体排序）；
  3. 网格、描边、手柄最后画，保持现在的顺序。
- **光栅状态**：DoubleSided → `CullMode.None`，否则 `CullMode.Back`；预先建好几种状态对象。
- 色调映射：输出前做简单的 ACES 或 Reinhard + 伽马（交换链若是 SRGB 格式就不用手动伽马），和现有灯光强度单位对齐。

## 4. 界面（照 Blender 的材质标签页）

属性面板新增一个“材质”标签页（红球图标）：

1. **预览球**（P2）：离屏渲染一个小球，用当前材质。
2. **材质槽列表**：显示槽序号和材质名；右侧 `+`、`-`、`v`（Specials：复制材质、粘贴材质、复制材质到选中物体、删除未使用的槽），以及上移/下移。
3. **数据块行**：浏览下拉（列出所有材质，带搜索）、名称输入框、用户数、Fake User、**新建**（空槽时）/ 复制、`X` 断开。
4. **Surface 面板**（类似 Principled BSDF）：基础色（色块 + 颜色选择器 + 贴图按钮）、金属度、糙度（滑条 0–1）、法线贴图 + 强度、自发光颜色和强度、Alpha。
5. **Settings 面板**：Alpha 模式（不透明 / 裁剪 / 混合）、裁剪阈值、背面剔除（=DoubleSided 取反）。
6. 所有字段改动立即刷新视口；文案加进 `UiText.cs`（由 program 写，需要时请 english 校对）。

## 5. 操作

| 操作 | 说明 | 阶段 |
|---|---|---|
| 新建材质 | 当前槽新建默认材质 | P0 |
| 断开材质（X） | 槽变空，回到默认灰 | P0 |
| 浏览指定已有材质 | 下拉选择 | P0 |
| 添加 / 删除槽 | `+` / `-` | P1 |
| 复制 / 粘贴材质 | 把参数复制进内部剪贴板，粘贴到当前材质 | P1 |
| 复制材质到选中物体 / Ctrl+L 关联材质 | 选中物体都用活动物体的材质 | P1 |
| 删除未使用的槽 | 没有子网格用的槽 | P1 |
| 清理未使用材质 | 用户数 0 且没有 Fake User 的删除 | P1 |
| 给面指定材质（编辑模式 Assign/Select/Deselect） | 需要面编辑模式 | P2 |
| 复制出单独的材质（Make Single User） | | P2 |

## 6. 视口模式

视口右上角加着色模式切换（照 Blender），`Z` 键也可以切：
- **实体（Solid）**：现在的简单光照，用 BaseColor，不采样贴图，速度快；
- **材质预览（Material Preview）**：完整 PBR + 贴图 + 内置环境光（P1 用 HDRI），可以选择是否使用场景灯光；
- 线框模式保留现状。

## 7. 撤销 / 重做

所有材质操作都走现有的撤销系统（参见 `08_开源项目借鉴.md` 的撤销部分）：
- 改参数：`SetMaterialPropertyCommand(material, 属性, 旧值, 新值)`，拖滑条时合并成一次（按下记录旧值，松开提交）；
- 新建 / 断开 / 指定：`AssignMaterialCommand(object, slotIndex, 旧材质, 新材质)`；
- 增删槽、复制到选中、清理未用：记录变更前后的槽列表和材质库快照。

## 8. 分阶段计划和验收（只用内置立方体）

### P0：能显示、能改
1. Material / MaterialSlot / SubMesh 数据模型 + 默认灰材质；
2. PBR Cook-Torrance 着色器，接 b1 灯光、b2 材质常量；
3. 属性面板材质标签：新建、断开、浏览、基础色、金属度、糙度、自发光、Alpha 模式和阈值、背面剔除；
4. Opaque / Mask / Blend 三种绘制路径 + 透明物体排序；
5. 撤销/重做接入。

验收：
- [ ] 新建立方体显示默认灰，属性面板显示 1 个空槽；
- [ ] 点“新建”，把基础色改红，视口立即变红；撤销后变回灰；
- [ ] 金属度 1、糙度 0.2：高光明显变亮、变锐，颜色带红色；糙度 1：高光散开；
- [ ] 自发光强度 >0：背光面也亮；
- [ ] 复制一个立方体并设成混合、Alpha 0.5，放在另一个前面：能看到后面的立方体，网格和描边仍正常；
- [ ] 两个立方体共用一个材质（浏览选中），改颜色两个一起变，用户数显示 2；
- [ ] 背面剔除关闭时，相机进入立方体内部能看到内壁；
- [ ] 编译 0 错误。

### P1：贴图和材质预览
导入 glTF / Assimp 材质和贴图（SRV、sRGB、mipmap、切线），法线贴图、AO；槽 `+`/`-`、复制粘贴、复制到选中、Ctrl+L、清理；实体 / 材质预览模式切换和 HDRI 环境光。
验收：内置立方体指定一张颜色贴图和法线贴图后显示正确；实体模式下不显示贴图；材质预览模式显示贴图；复制粘贴后两个材质参数一致但独立。

### P2：进阶
编辑模式按面 Assign/Select/Deselect、预览球、Make Single User、Link Data/Object、贴图 UV 变换（KHR_texture_transform），以后再考虑 Clearcoat、Transmission 和简单节点。
验收：立方体顶面指定第二个材质后只有顶面变色，Select 能选中顶面。

## 9. 分工
- 代码：**program**（唯一写代码和提交的人）；
- 文档更新：novel（program 点名时）；
- 文案校对：english（program 点名时）。

---

# 第三部分：技术路线与参考实现

> 用户已确认这条路线，program 按它实现。

## 阶段 1（现在，P0）：固定参数 PBR
- 参数照 Principled BSDF 的子集：基础色、金属度、糙度、折射率（IOR，默认 1.5 → F0 = ((IOR−1)/(IOR+1))² = 0.04）、Alpha、自发光（颜色 × 强度）。
- 着色模型照 **Filament 材质文档**（<https://google.github.io/filament/Filament.html>）：
  - 法线分布：GGX（Trowbridge-Reitz）`D = α² / (π((n·h)²(α²−1)+1)²)`，α = 糙度²；
  - 可见性：Smith GGX 高度相关（height-correlated）`V = 0.5 / (NoL·√(NoV²(1−α²)+α²) + NoV·√(NoL²(1−α²)+α²))`；
  - 菲涅尔：Schlick `F = F0 + (1−F0)(1−v·h)⁵`；金属 `F0 = 基础色`，电介质用 IOR 算出的 F0；
  - 漫反射：Lambert（`基础色·(1−金属度)/π`），以后可换 Burley；
  - 能量：单次散射 GGX 在高糙度会损失能量，Filament 的多次散射补偿（基于 DFG LUT）放到阶段 2 和 IBL 一起做；漫反射乘 (1−F) 保证不超过 1。
- 对照 **glTF Sample Viewer**（<https://github.com/KhronosGroup/glTF-Sample-Viewer>）做验证：同一组金属度/糙度参数在两边的明暗和高光形状应接近。
- 实体（Solid）模式不走 PBR，保持 `8fe7aea` 的 Blender 灰色工作室光。

## 阶段 2（P1）：贴图 + IBL / HDRI
- 贴图：基础色（sRGB）、法线（切线空间）、金属度-糙度（G 糙度 B 金属）、AO（R）、自发光（sRGB）；生成 mipmap。
- IBL：split-sum 近似——预过滤的镜面环境图（按糙度分 mip）+ 漫反射辐照度（球谐或小立方体图）+ BRDF LUT（DFG），做法同 Filament 和 glTF Sample Viewer；材质预览模式用内置 HDRI。

## 阶段 3（P2）：节点材质与自定义着色器
- 节点材质编辑器基于 **MaterialX**（<https://github.com/AcademySoftwareFoundation/MaterialX>），由它的 shader generator 生成 HLSL。
- 允许用户给材质挂自己写的 HLSL 着色器（约定好输入的 cbuffer 和贴图槽）。

## 许可证规则
- Filament、glTF Sample Viewer、MaterialX 都是 **Apache-2.0**：可以参考、改写公式和代码，要在源码注释和致谢里注明出处。
- Blender / EEVEE 是 **GPL**：只借鉴思路和界面，**绝不复制代码**。
