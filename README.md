<div align="center">

# 🧊 PolyView

**A lightweight, fast and good-looking 3D model viewer for Windows**

Built with C# / WPF and Direct3D 11

<p>
  <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 10">
  <img src="https://img.shields.io/badge/C%23-13-239120?style=for-the-badge&logo=csharp&logoColor=white" alt="C#">
  <img src="https://img.shields.io/badge/WPF-Fluent_UI-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="WPF">
  <img src="https://img.shields.io/badge/Direct3D-11-107C10?style=for-the-badge&logo=directx&logoColor=white" alt="Direct3D 11">
  <br>
  <img src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6?style=flat-square&logo=windows11" alt="Platform">
  <img src="https://img.shields.io/badge/IDE-Visual%20Studio%202026-5C2D91?style=flat-square&logo=visualstudio" alt="Visual Studio 2026">
  <img src="https://img.shields.io/badge/glTF-2.0-87C540?style=flat-square&logo=khronosgroup&logoColor=white" alt="glTF 2.0">
  <img src="https://img.shields.io/badge/status-early%20development-orange?style=flat-square" alt="Status">
  <img src="https://img.shields.io/badge/license-TBD-lightgrey?style=flat-square" alt="License">
  <img src="https://img.shields.io/badge/PRs-welcome-brightgreen?style=flat-square" alt="PRs welcome">
</p>

**English** | [Simplified Chinese](README.zh-CN.md)

[✨ Features](#-features) •
[🛠️ Tech Stack](#️-tech-stack) •
[🗺️ Roadmap](#️-roadmap) •
[🚀 Getting Started](#-getting-started) •
[📁 Structure](#-project-structure)

</div>

## ✨ Highlights

**🤖 Built-in MCP: AI can drive PolyView directly**

- **Zero config:** PolyView starts a local MCP server at `http://127.0.0.1:47800/mcp` (Streamable HTTP, localhost only) when the app launches.
- **Scan & one-click connect:** In **Edit › Preferences › AI / MCP**, click **Scan** to list the AI clients on this computer (installed, running, connected). Tick the ones you want and click **Connect**; PolyView merges a `polyview` entry into their config files without touching other entries, and keeps a one-time `.polyview.bak` backup before the first change. Clients written automatically today: Cursor, Claude Code, Claude Desktop (via `npx mcp-remote`), VS Code, Windsurf, Trae, Gemini CLI and Codex CLI. Cherry Studio and Doubao need the config pasted in manually.
- **Any MCP client:** One click copies the HTTP config or the stdio (`npx mcp-remote`) config, which can be pasted into any MCP-capable client (for example, using DeepSeek and other models through Cherry Studio).
- **Command registry:** Inspired by Blender operators, every feature is registered as a command (such as `object.add_primitive` or `file.export`). AI calls any command through `list_commands` / `describe_command` / `run_command`; `get_scene`, `get_viewport_screenshot` (lets the AI see the viewport) and the shortcut tools `add_primitive`, `add_light`, `set_transform` and `import_model` can be called directly. Registered commands: add primitive / light, duplicate, delete (asks you to confirm), transform, show / hide, select, shading, view, save / save as / open (open asks you to confirm), import, and export to FBX / OBJ / glTF / GLB / STL / PLY / DAE. The **● MCP** item in the top menu bar shows the server status and the last 20 calls.

```json
{ "mcpServers": { "polyview": { "url": "http://127.0.0.1:47800/mcp" } } }
```

Design: [docs/11 MCP integration design](docs/11_MCP%E6%8E%A5%E5%85%A5%E8%AE%BE%E8%AE%A1.md)


---

> [!NOTE]
> PolyView is in **early development (Phase 0)**. Features below are planned unless checked in the roadmap.
>
> **Working today (master):** open OBJ / STL / GLB / glTF (all meshes and primitives, node hierarchy transforms, base color texture & vertex colors; skinned models shown in bind pose), drag & drop a model file anywhere on the window, orbit / pan / zoom.
>
> **On branch `feat/import-xiaoB`:** FBX / DAE / three.js JSON, Draco-compressed GLB and WebP textures, Blender-style navigation (middle mouse, numpad views, ortho), grid floor, on-demand rendering with an FPS readout. See [Supported Formats](#-supported-formats).

## ✨ Features

| | Feature | Description |
| :---: | --- | --- |
| 📂 | **Multi-format import** | OBJ, STL, GLB and glTF (via the Open dialog, drag & drop onto the window, or `PolyView.exe <file>`) |
| 🖱️ | **Smooth navigation** | Orbit, pan and zoom-to-cursor with the mouse |
| 🎨 | **Shading modes** | Solid, wireframe and lit shading |
| 🎯 | **Picking** | Click an object to see its name, vertex / triangle count and bounding box |
| 🌙 | **Modern UI** | Fluent design, dark theme, dockable panels |
| ⚡ | **Stable memory** | No memory spikes or stutter, even with large models |
| 🖥️ | **High-DPI ready** | Crisp viewport at 100% / 150% / 200% scaling |

## 📦 Supported Formats

"Supported (master)" = merged into master; "Supported (feat/import-xiaoB)" = only on XiaoB's branch, waiting for Ethan to merge; "In progress" = being written; "Planned" = not started. Details and measured numbers: [docs/04 Rendering principles & optimization](docs/04_%E6%B8%B2%E6%9F%93%E5%8E%9F%E7%90%86%E4%B8%8E%E4%BC%98%E5%8C%96.md).

| Format | Importer | Status | Notes |
| --- | --- | --- | --- |
| .glb / .gltf | SharpGLTF (falls back to Assimp) | Supported (master) | Meshes, node transforms, base color texture, vertex colors, double-sided; skinned models in bind pose |
| .obj (+.mtl) | Custom parser; on the branch Assimp first | Supported (master) | MTL colors and map_Kd textures |
| .stl | Custom parser | Supported (master) | Binary / ASCII; Z-up converted to Y-up on the branch |
| .fbx / .dae | Assimp 6.0.2 (Silk.NET.Assimp) | Supported (feat/import-xiaoB) | Node transforms, generated normals, diffuse color and textures (incl. embedded FBX textures) |
| .3ds / .ply | Assimp | Supported (feat/import-xiaoB), not specifically tested | Same as above |
| three.js JSON (.json, `toJSON()` / editor export) | Custom parser | Supported (feat/import-xiaoB) | Format 4.x, BufferGeometry + Box / Sphere / Cylinder / Plane, node matrices, base color. Most three.js projects ship .glb / .gltf, which already work |
| GLB + KHR_draco_mesh_compression | Google Draco 1.5.7 decoder (Apache-2.0, built from source) | Supported (feat/import-xiaoB), .glb only | Decoded and rewritten before SharpGLTF reads it |
| GLB + EXT_texture_webp | SharpGLTF + Windows WIC | Supported (feat/import-xiaoB) | Shown when the system WebP codec is installed, otherwise skipped safely |

## 🛠️ Tech Stack

| Area | Choice |
| --- | --- |
| Runtime | .NET 10 (`net10.0-windows`) |
| UI | WPF + [WPF UI](https://github.com/lepoco/wpfui) (Fluent) + [AvalonDock](https://github.com/Dirkster99/AvalonDock) |
| MVVM | [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| Rendering | Direct3D 11 via [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows), HLSL shaders |
| Viewport hosting | `D3DImage` (no airspace issues, WPF controls can overlay the viewport) |
| Model loading | [SharpGLTF](https://github.com/vpenades/SharpGLTF) for glTF / GLB; lightweight custom OBJ / STL parsers |

```mermaid
flowchart LR
    A[WPF UI<br/>Fluent / MVVM] -->|commands, camera, ids| B[PolyView.Render<br/>Vortice D3D11]
    B -->|shared surface| C[D3DImage<br/>viewport]
    D[Loaders<br/>SharpGLTF / OBJ / STL] -->|background thread| B
    B --> E[(GPU)]
```

<details>
<summary><b>💡 Why <code>D3DImage</code> instead of <code>HwndHost</code>?</b></summary>

<br>

`HwndHost` embeds a native child window that always draws on top of WPF content, so overlays, popups,
transparency and rounded corners break around the viewport. Rendering into a shared surface shown through
`D3DImage` keeps the viewport a normal WPF element, which is what makes a polished UI possible.

</details>

## ⚡ Memory & Performance Principles

- ✅ Vertex / index data is uploaded to the GPU, then the CPU-side copy is released immediately
- ✅ Large buffers use `ArrayPool<T>` / `NativeMemory` to avoid Large Object Heap churn
- ✅ **Zero allocations per frame** in the render loop (struct math, no LINQ / closures / string building)
- ✅ All GPU resources are `IDisposable` and released explicitly, never left to the GC
- ✅ Model loading runs on a background thread; GPU upload happens on the render thread
- ✅ Each phase is verified by opening / closing the same model 20 times and checking memory returns to baseline

## 🗺️ Roadmap

- [ ] **Phase 0 – Skeleton:** WPF window + D3D11 viewport, correct resize & DPI handling, clean shutdown
- [ ] **Phase 1 – Test window:** load GLB / glTF, orbit / pan / zoom, solid & wireframe, FPS / triangle / memory overlay
- [ ] **Phase 2 – Main UI:** Fluent theme, scene tree, viewport, property panel, multi-file scenes, picking
- [ ] **Phase 3 – Formats & polish:** OBJ / STL, view cube / axis gizmo, recent folders, loading progress
- [ ] **Later:** measurement, section / exploded views, embedding the viewport in other WPF apps

## 🚀 Getting Started

### Requirements

- 🪟 Windows 10 / 11
- 📦 [.NET 10 SDK](https://dotnet.microsoft.com/download)
- 🧰 Visual Studio 2026 with the **.NET desktop development** workload
- 🎮 A GPU supporting Direct3D 11

### Build & Run

```bash
git clone <this-repo-url>
cd 1023_PolyView/PolyView
dotnet build PolyView.slnx
dotnet run --project PolyView/PolyView.csproj
```

Or open `PolyView/PolyView.slnx` in Visual Studio and press **F5**.

## 📁 Project Structure

```
1023_PolyView/
├── README.md
├── README.zh-CN.md
├── PolyView/
│   ├── PolyView.slnx        # Open this in Visual Studio 2026
│   └── PolyView/            # WPF application project
├── Models/                  # Test models (flat; sources in Models/SOURCE.txt)
└── docs/                    # All docs, numbered; index in docs/AGENTS.md
    ├── AGENTS.md            # Hand-over notes and doc index
    ├── 01_*.md   # Development plan (Chinese)
    ├── 02_*.md   # Animation resource sites (Chinese)
    ├── 03_*.md   # 3D display principles & optimization draft (Chinese)
    └── 04_*.md   # Rendering pipeline, optimizations, measured capacity (Chinese)
```

## 🧩 Sample Models

Test models come from CC0 (public domain) sources such as [Kenney](https://kenney.nl/),
[Quaternius](https://quaternius.com/) and [Poly Haven](https://polyhaven.com/). Large files are not committed.

## 🙏 Acknowledgements

[WPF UI](https://github.com/lepoco/wpfui) · [AvalonDock](https://github.com/Dirkster99/AvalonDock) ·
[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) · [SharpGLTF](https://github.com/vpenades/SharpGLTF) ·
[CommunityToolkit](https://github.com/CommunityToolkit/dotnet)

## 📄 License

To be decided (MIT recommended). Third-party libraries keep their own licenses.

<div align="center">

<sub>Made with ❤️ and C#</sub> · [⬆ Back to top](#-polyview)

</div>
