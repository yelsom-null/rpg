# rpg

Unity project scaffold with [MCP for Unity](https://github.com/CoplayDev/unity-mcp) wired in, so an AI assistant (Claude, etc.) can control the Unity Editor via the Model Context Protocol.

## What's here

- `Assets/`, `Packages/manifest.json`, `ProjectSettings/ProjectVersion.txt` — minimal Unity project skeleton. Unity regenerates the rest of `ProjectSettings/` on first open.
- `Packages/manifest.json` — includes `com.coplaydev.unity-mcp` as a git-URL package dependency, pinned to `main`.
- `.mcp.json` — project-scoped Claude Code MCP config pointing at the MCP for Unity server (run via `uvx`).

## Setup

1. **Install Unity** (2021.3 LTS or newer) via [Unity Hub](https://unity.com/download) and open this folder as a project. `ProjectSettings/ProjectVersion.txt` targets `6000.0.36f1` — adjust it (or let Unity Hub prompt you) to whatever version you have installed.
2. On first open, Unity's Package Manager resolves the `com.coplaydev.unity-mcp` package from git. A setup wizard should appear — it checks for Python 3.10+ and [`uv`](https://docs.astral.sh/uv/), then lets you configure detected MCP clients (Claude Code, Claude Desktop, Cursor, etc.) with one click via **Window → MCP for Unity**.
3. Claude Code is already pre-configured via `.mcp.json` at the repo root (stdio transport, `uvx --from mcpforunityserver mcp-for-unity`). Once the Unity Editor is running with the MCP for Unity package loaded, Claude Code should connect automatically.
4. Try a prompt like *"Create a red cube at the origin and add a Rigidbody."*

See the [MCP for Unity docs](https://coplaydev.github.io/unity-mcp/) for troubleshooting and advanced configuration (multi-instance routing, tool groups, remote server auth).
