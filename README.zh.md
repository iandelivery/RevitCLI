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

多个 Revit 实例（不同版本或相同版本）可以同时运行。每个桥接器从其版本对应的端口范围中自动选择端口，并在 revit-cli 数据目录中注册自身。

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│ Revit 2020   │     │ Revit 2022   │     │ Revit 2022   │
│ :5021        │     │ :5041        │     │ :5042        │
│ PID 1234     │     │ PID 5678     │     │ PID 9012     │
└──────────────┘     └──────────────┘     └──────────────┘
       │                    │                     │
       └────────────────────┴─────────────────────┘
                            │
                   %LOCALAPPDATA%\revit-cli\instances\
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

## 本地存储位置

Revit CLI 使用**级联目录策略**在本地保存运行期状态。实例注册表和 Schema 缓存共享相同的基础目录，按以下优先级解析：

| 优先级 | 基础目录 | 适用场景 |
|--------|---------|----------|
| 1 | `%REVIT_CLI_DATA_DIR%` | 显式覆盖（无头、CI、自定义挂载点） |
| 2 | `%LOCALAPPDATA%\revit-cli\` | Windows 本地应用数据最佳实践 |
| 3 | `%USERPROFILE%\.revit-cli\` | 符合 CLI 工具惯例的点文件夹（可绕过大多数组策略） |
| 4 | `<exe 所在目录>\.revit-cli\` | 便携模式——无需用户配置目录 |

桥接器和 CLI 都按顺序尝试每个路径，首次使用时自动创建，失败则继续尝试下一个。如需确认实际使用的路径，运行 `revit-cli.exe configure check`。

### 实例注册表（由桥接器写入）

每个运行中的桥接器都会向 `<数据目录>/instances/` 写入一个 JSON 描述文件，供 CLI 客户端发现：

```
%LOCALAPPDATA%\revit-cli\instances\
  ├── revit-2020-1234.json   # 每个运行中的 Revit 一个文件
  ├── revit-2022-5678.json
  └── revit-2022-9012.json
```

可用 `revit-cli.exe list` 查看当前内容。

### Schema 缓存（由 CLI 客户端写入）

CLI 客户端会把命令 schema（通过 `GET /api/commands` 获取）在本地缓存起来，TTL 为 30 分钟，并使用 ETag 实现条件请求。桥接器不可达时会回退到陈旧缓存。

```
%LOCALAPPDATA%\revit-cli\cache\
  ├── localhost_5041_schema.json
  └── localhost_5041_schema.etag
```

## 项目

| 目录 | 语言 | 描述 |
|-----------|----------|-------------|
| [`bridge/`](bridge/) | C# (.NET) | 在 Revit 内运行 HTTP 服务器的插件，将 Revit API 操作暴露为 CLI 命令 |
| [`client/`](client/) | Go | 独立的 CLI 客户端，通过 HTTP/SSE 向桥接器发送命令 |

## 快速开始

### 方式 1 — 下载 release

1. 从 [Releases](../../releases) 页面下载最新的 `revit-cli-<version>.zip`。
2. 解压到任意目录（例如 `C:\revit-cli\`）。压缩包内含 `revit-cli.exe` 客户端、供 AI 代理使用的 `SKILL.md`，以及包含全部 4 个 Revit 版本桥接器的 `bridge/` 文件夹。
3. 为本机所有 Revit 安装桥接器：
   ```powershell
   cd C:\revit-cli
   .\revit-cli.exe configure setup
   ```
   命令会扫描 Windows 注册表，然后把每个版本对应的桥接器文件复制到对应 Revit 版本的插件目录，并写入正确的端口。
4. 启动 Revit，点击 **Revit CLI Bridge** 功能区选项卡中的 **AI Mode Toggle** 按钮。
5. 测试连接：
   ```powershell
   .\revit-cli.exe ping
   ```

### 方式 2 — 从源码编译

**编译 Bridge（C# 插件，覆盖所有 Revit 版本）：**

```powershell
cd bridge
.\build.ps1
```

产物会按版本输出到 `bridge/dist/Revit20XX/`。加 `-Deploy` 参数可自动安装到所有检测到的 Revit；加 `-Clean` 会在编译前清空 `dist/` 和 `obj/`。

**编译 Go 客户端：**

```bash
cd client
go build -o revit-cli.exe ./cmd/revit-cli

# 或交叉编译所有平台：
./build.sh --all
```

**手动安装桥接器**（如果没用 `-Deploy`）：

1. 把 `bridge/dist/Revit<年份>/` 下的 DLL 和 `RevitCliBridge.addin` 复制到：
   - `%APPDATA%\Autodesk\Revit\Addins\<version>\RevitCliBridge\`
2. 启动 Revit，点击 **Revit CLI Bridge** 功能区选项卡中的 **AI Mode Toggle** 按钮。

### 运行命令

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
