# Revit CLI Bridge

一个命令行接口工具包，使 AI Agent和自动化脚本能够通过 HTTP API 驱动 Autodesk Revit。

## 架构

```
┌──────────────┐                          ┌──────────────────────────────┐
│              │   POST /api/execute      │                              │
│              │   Accept: text/event-    │     RevitCliBridge           │
│  CLI 客户端   │   stream                 │     (Revit 插件)              │
│  (Go)        │ ───────────────────────► │                              │
│              │   SSE 事件流              │  接收 HTTP 请求，              │
│              │◄───────────────────────  │  在 Revit 主线程执行，          │
│              │   event: accepted        │  流式传输进度。                 │
│              │   event: progress        │                              │
│              │   event: completed       │                              │
└──────────────┘                          └──────────────────────────────┘
```

### 多实例架构

多个 Revit 实例（不同版本或相同版本）可以同时运行。每个桥接器从其版本对应的端口范围中自动选择端口，并在 `%AppData%\revit-cli\instances\` 中注册自身。

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ Revit 2020   │     │ Revit 2022   │     │ Revit 2022   │
│ :5021        │     │ :5041        │     │ :5042        │
│ PID 1234     │     │ PID 5678     │     │ PID 9012     │
└──────────────┘     └──────────────┘     └──────────────┘
       │                    │                     │
       └────────────────────┴─────────────────────┘
                            │
                   %AppData%\revit-cli\instances\
                     ├── revit-2020-1234.json
                     ├── revit-2022-5678.json
                     └── revit-2022-9012.json
                            │
                   ┌────────┴────────┐
                   │  CLI 客户端      │
                   │  revit-cli list │
                   │  --revit 2022   │
                   │  --pid 5678     │
                   └─────────────────┘
```

## 项目

| 目录 | 语言 | 描述 |
|-----------|----------|-------------|
| [`bridge/`](bridge/) | C# (.NET) | 在 Revit 内运行 HTTP 服务器的插件，将 Revit API 操作暴露为 CLI 命令 |
| [`client/`](client/) | Go | 独立的 CLI 客户端，通过 HTTP/SSE 向桥接器发送命令 |

## 快速开始

### 1. 安装桥接器（Revit 插件）

**方式 A：自动安装（推荐）**

先编译好 Go 客户端（见下方步骤 2），得到 `revit-cli.exe`，然后运行：

```bash
revit-cli.exe configure setup
```

命令会扫描 Windows 注册表找到所有已安装的 Revit 版本，然后从 `bridge/Revit<年份>/` 子目录中复制对应版本的桥接器文件到每个版本的插件目录，并写入正确的端口配置。

**方式 B：手动安装**

1. 编译所有支持的 Revit 版本：
   ```powershell
   cd bridge
   .\build.ps1
   ```
   产物会按版本输出到 `bridge/dist/Revit20XX/`。加 `-Deploy` 参数可以自动将插件安装到所有检测到的 Revit。
2. 将输出的 DLL 和 `RevitCliBridge.addin` 复制到 Revit 插件目录：
   - `%APPDATA%\Autodesk\Revit\Addins\<version>\RevitCliBridge\`
3. 启动 Revit，点击 "Revit CLI Bridge" 功能区选项卡中的 "AI Mode Toggle" 按钮

### 2. 编译 Go 客户端

```bash
cd client
go build -o revit-cli.exe ./cmd/revit-cli

# 或交叉编译所有平台：
./build.sh --all
```

### 3. 运行命令

```bash
# 列出运行中的 Revit 实例
revit-cli.exe list

# 测试连接（单实例时自动发现）
revit-cli.exe ping

# 连接到特定 Revit 版本
revit-cli.exe --revit 2022 ping

# 列出可用命令
revit-cli.exe commands

# 查询元素
revit-cli.exe elements -c OST_Walls

# 创建墙体
revit-cli.exe create_wall --start-x 0 --start-y 0 --end-x 5000 --end-y 0 -l 3001

# 查看 Revit API 参考（用于未覆盖的操作）
revit-cli.exe llms
```

## 功能特性

- **60+ 内置命令** — 创建墙体/门/窗、查询元素、修改参数、导出视图、管理文档
- **SSE 实时流** — 长时间运行操作的实时进度更新
- **Schema 发现** — 客户端自动从桥接器发现可用命令
- **多实例支持** — 同时运行多个 Revit 版本，自动端口分配
- **实例发现** — `--revit`、`--pid` 和 `list` 命令用于定位特定实例
- **llms.txt API 参考** — AI 代理可发现原始 Revit API 元素，使用 `execute_raw` 作为回退
- **插件系统** — 通过 `IBridgeCommand` 接口支持第三方命令处理器
- **多版本支持** — Revit 2019、2020、2021、2022
- **试运行模式** — 预览操作而不修改文档
- **自动安装** — `configure setup` 可在所有检测到的 Revit 版本中安装桥接器

## 端口分配

启用 `auto_port`（默认）时，每个 Revit 版本获得专用端口范围：

| Revit 版本 | 基础端口 | 范围 |
|------------|---------|------|
| 2019 | 5011 | 5011-5019 |
| 2020 | 5021 | 5021-5029 |
| 2021 | 5031 | 5031-5039 |
| 2022 | 5041 | 5041-5049 |

端口 5000 保留为传统回退端口。如果版本范围内的所有端口都被占用，桥接器将回退到配置的 `port` 值或操作系统分配的临时端口。

## 文档

| 文档 | 说明 |
|------|------|
| [桥接器 README](bridge/README.md) | 编译、安装、配置和使用 Revit 插件 |
| [桥接器架构文档](bridge/ARCHITECTURE.md) | 完整协议规范与内部架构 |
| [客户端 README](client/README.md) | 编译和使用 Go CLI 客户端 |

## 许可证

MIT — 详见 [LICENSE](LICENSE)
