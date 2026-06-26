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

仓库根目录的 `build.ps1` 会在一次执行中同时编译 C# Bridge 和 Go 客户端，并打包成可直接发布的 zip，与 GitHub Release 工作流完全一致。

**前置条件**

| 工具 | 版本要求 | 用途 |
|------|---------|------|
| Windows | 10+ | Bridge 目标平台 `x64`；Go 构建使用 `GOOS=windows` |
| PowerShell | 5.1+（推荐 PowerShell 7+） | 执行 `build.ps1` |
| .NET SDK | 6.0.x 和 7.0.x | 编译覆盖所有 Revit 版本的 C# Bridge |
| Go | 1.21 或更新（与 `go.mod` 一致） | 编译 `revit-cli.exe` |
| Git | 任意较新版本 | 通过 `git describe` 读取版本号 |

构建前请先确认工具在 `PATH` 中可用：

```powershell
dotnet --version     # 应输出 6.x 或 7.x
go version           # 应输出 go1.21 或更新版本
git --version
```

**步骤 1 — 执行统一构建脚本**

在仓库根目录执行：

```powershell
cd C:\path\to\revit-cli-opensource
.\build.ps1
```

脚本按三个阶段运行，完全对应 CI 发布流水线：

1. **编译 Bridge**：依次构建所有受支持的 Revit 版本（2019、2020、2021、2022）。产物输出到 `bridge/dist/Revit20XX/`，并为每个版本生成包含正确端口（5011、5021、5031、5041）的 `.config/cli_bridge_setting.json`。
2. **编译 Go 客户端**：`go vet` + `go build`，并通过 `-ldflags "-X main.Version=…"` 注入版本号。
3. **打包**：在仓库根目录的 `dist/` 下生成三类 zip：
   - `revit-cli-<version>.zip` — 完整包（客户端 + SKILL.md + 所有 Bridge 版本）
   - `revit-cli-client-<version>.zip` — 仅客户端 + SKILL.md
   - `RevitCliBridge-Revit<year>-<version>.zip` — 单个 Bridge 版本

**步骤 2 — 验证构建结果**

成功执行时，脚本会用青色/黄色输出每个阶段，最后以绿色的 `Build Complete` 汇总收尾。退出码为 `0` 表示所有阶段都成功。

```powershell
# 检查退出码是否为零
echo $LASTEXITCODE   # 应输出 0

# 确认 zip 产物已生成
Get-ChildItem .\dist\*.zip
```

预期输出（以 `v1.0.0` 标签为例）：

```
revit-cli-1.0.0.zip
revit-cli-client-1.0.0.zip
RevitCliBridge-Revit2019-1.0.0.zip
RevitCliBridge-Revit2020-1.0.0.zip
RevitCliBridge-Revit2021-1.0.0.zip
RevitCliBridge-Revit2022-1.0.0.zip
```

**常用参数**

| 参数 | 作用 |
|------|------|
| `-RevitVersions "2021,2022"` | 仅编译指定的 Revit 版本 Bridge |
| `-SkipBridge` | 复用现有 `bridge/dist/` 产物，只构建/打包客户端 |
| `-SkipClient` | 复用现有 `revit-cli.exe`，只构建/打包 Bridge |
| `-SkipPackage` | 执行构建但跳过打包 zip（适合快速迭代） |
| `-SkipVet` | 跳过 `go vet`，加快客户端编译速度 |

使用示例：

```powershell
# 仅快速迭代 Go 客户端
.\build.ps1 -SkipBridge

# 仅编译并打包 Revit 2022 Bridge
.\build.ps1 -RevitVersions "2022" -SkipVet
```

**单独构建某个组件**

根目录的 `build.ps1` 是一站式的推荐脚本，但当你只想做本地快速迭代时，仍然可以单独构建某一个组件。每个子项目都自带 `build.ps1`，执行的就是根脚本里针对该组件的那部分步骤。

```powershell
# 仅构建 Bridge（所有 Revit 版本，不打包）
cd bridge
.\build.ps1

# 仅构建 Go 客户端
cd ..\client
.\build.ps1
```

两个脚本接受同样的 `-SkipVet` 等开关，产物输出目录也与根脚本一致（分别是 `bridge/dist/Revit20XX/` 和 `client/revit-cli.exe`），所以任意一条路径生成的产物都可以互换使用。发布构建请用根目录的 `build.ps1`；紧密的内循环开发则推荐使用子项目脚本。

**故障排查**

| 现象 | 可能原因 | 解决方法 |
|------|----------|----------|
| `[ERROR] Go is not installed or not on PATH.` | 当前 PowerShell 会话找不到 `go` | 从 <https://go.dev/dl/> 安装 Go，或执行 `winget install GoLang.Go` 后重新打开终端 |
| `[ERROR] dotnet CLI not found.` | .NET SDK 未安装或未加入 `PATH` | 安装 .NET 6.0 与 7.0 SDK（编译多版本 Revit 都需要） |
| `dotnet build` 报 `MSB4019: The imported project … was not found.` | 缺少 `RevitAPI.dll` / `RevitAPIUI.dll` 引用 | csproj 期望 Revit SDK 程序集位于 `PATH` 或标准路径 `C:\Program Files\Autodesk\Revit <year>\` 下 |
| `git describe` 返回 `dev` | 当前提交没有匹配的 git 标签 | 给当前提交打标签（例如 `git tag v1.0.0`），或在脚本中显式指定版本；否则 zip 名称会使用 `dev` |
| Bridge 构建成功但 `revit-cli.exe --version` 烟雾测试失败 | 新旧二进制路径冲突 | 删除 `client/revit-cli.exe` 后重新执行 `.\build.ps1` |
| PowerShell 5.1 下 `Compress-Archive` 生成空 zip | PowerShell 5.1 处理 `-Path` 通配符的历史 bug | 改用 PowerShell 7+（`pwsh`），或改用 `tar -a -cf <name>.zip -C staging .` |
| `Access to the path 'bridge/dist' is denied.` | 上一次 Revit 进程或构建进程仍在占用文件 | 关闭任何指向 `bridge/dist/` 的资源管理器窗口后重新执行 |

**手动安装 Bridge**（如果你没有走自动安装流程）：

1. 把 `bridge/dist/Revit<year>/` 下的 DLL 和 `RevitCliBridge.addin` 复制到：
   - `%APPDATA%\Autodesk\Revit\Addins\<version>\RevitCliBridge\`
2. 启动 Revit，Bridge 会以 **AI Mode（默认开启）** 自动运行（由 `.config/cli_bridge_setting.json` 中的 `enabled` 控制，默认为 `true`）。**Revit CLI Bridge** 功能区选项卡中的 **AI Mode Toggle** 按钮仅在需要关闭 Bridge 时使用。

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

### 原始执行模式

`execute_raw` 命令（用于在 Revit API 上执行任意 C# 或 Python 代码）出于安全考虑**默认禁用**。`raw-mode` 命令允许你在运行时切换该设置，而无需修改配置文件或重启 Revit：

```bash
# 查看当前状态
revit-cli.exe raw-mode

# 启用原始执行
revit-cli.exe raw-mode --enable

# 禁用原始执行
revit-cli.exe raw-mode --disable
```

该命令通过 `GET /api/raw-mode` 查询当前状态，通过 `POST /api/raw-mode` 进行切换。设置仅保存在内存中——重启 Revit 后会恢复为 `cli_bridge_setting.json` 中 `allow_raw_execution` 的值。若需持久化更改，请直接编辑该文件。

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
