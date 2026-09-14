# 管线综合（MEP Coordination）新命令计划

> 面向 `bridge/`（Revit 插件）新增命令。Go 客户端通过 schema 动态发现命令，因此只需在 C# 侧实现 handler，无需改动 `client/` 即可自动生效（`CommandRouter` 反射发现 + `GET /api/commands` schema 下发）。

## 0. 现状审核（已确认的事实）

| 能力域 | 已有 | 缺失 |
|--------|------|------|
| 风管 Duct | `create_duct` / `get_ducts` / `get_duct_types` / `get_duct_system_types` / `modify_duct` + 6 种管件 | — |
| 桥架 CableTray | `create_cable_tray` / `get_cable_trays` / `get_cable_tray_types` / `modify_cable_tray` + 6 种管件 | — |
| **水管/管道 Pipe** | **无任何命令** | **创建、查询、类型、系统类型、修改、管件全缺（最大缺口）** |
| 管线分析 | 仅 `set_section_box`（可自动取元素包围盒） | 碰撞检查、净高/净空分析 |
| 通用能力 | DryRunTransaction、PagedResultBuilder、PaginatedQueryHandler、HandlerUtilities、SSE 进度、schema/llms.txt 自动生成 | — |

接入约定（与现有 handler 保持一致）：
- 继承 `DocumentCommandBase`（自动做 `ActiveUIDocument` 判空）；只读命令 `SupportsDryRun => false`。
- 参数用 `CommandParamSchema[]` 声明 + `[Param]` POCO + `TryBind`；描写/单位一律 mm。
- 查询类继承 `PaginatedQueryHandler<object>`，复用 `ItemsProperty`/`HandlerUtilities`。
- 修饰类操作包在 `DryRunTransaction(doc, …, cmd.DryRun)` + `ConfigureFailureHandling()` 里。
- 纯逻辑（单位换算、connector 解析、碰撞对计算、净高汇总）抽成工具类，配套 xUnit 测试（对齐 `DuctParamBinderTests` 等现有风格）。
- **MEP Util 三专业统一**（本次计划核心）：Duct/CableTray/Pipe 同为 `MEPCurve`，连接器与几何逻辑与专业无关，必须抽共享核心一次复用，三专业各自只保留薄门面，**杜绝第三份重复**（现状 `DuctUtils`/`CableTrayUtils` 已 ~90% 雷同，详见建议 A）。
- **版本兼容策略**：当前锁定 Revit **2019–2022**（csproj `R19–R22`）。本计划涉及的 MEP API（`Pipe.Create`、`New*Fitting`、`Connector.CoordinateSystem`、`MEPCurve.ConnectorManager`、`RBS_*` 参数）在 2019–2022 **全部稳定，无需按版本分叉**。唯一 breaking change 是单位换算（Revit 2021 起 `DisplayUnitType`→`UnitTypeId`），已集中在 `ConversionUtilities` 用 `#if (R19||R20)/(R21||R22)` 包裹。**硬约束：共享核心与三专业门面一律走现有 `.MillimeterToFeet()`/`.FeetToMillimeter()` 扩展，绝不直调 `UnitUtils`**，即可零改动跨 2019–2022。扩展到 2023/2024 = 补 csproj `R23/R24` 配置 + `ConversionUtilities` 加 `#if (R23||R24)` 分支（复用 `UnitTypeId` 路径），MEP 层仍无需改动；不建议为此做反射式"版本适配器"（过度设计）。

关键 Revit API（已验证，context7 不可用，改用官方文档检索核对）：
- `Pipe.Create(Document, systemTypeId, pipeTypeId, levelId, startPoint, endPoint)` — 两点建管 [$TRAE_REF](https://help.autodesk.com/view/RVT/2025/ENU/?guid=04817280-4044-112c-d229-59e8a50b1c95)；还有连接两个 Connector 的重载。
- 管道类型收集 `FilteredElementCollector.OfClass(typeof(PipeType))`；管道系统类型为 `PipingSystemType`。
- 管件创建走**通用** `doc.Create.NewElbowFitting / NewTeeFitting / NewCrossFitting / NewTransitionFitting / NewUnionFitting / NewTakeoffFitting`，参数为 `Connector`，对 Duct/Pipe/CableTray/Conduit 的连接器通用 [$TRAE_REF](https://help.autodesk.com/view/RVT/2023/ITA/?guid=Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_MEP_Engineering_Family_Creation_html)[$TRAE_REF](https://rvtdocs.com/2025/1dcbaa41-5881-b807-b333-10855acb408e/)。

---

## 1. 计划结构与优化建议（审核结论）

优先级排序原则：**先补齐水专业能力（与风管/桥架对等），再做差异化分析命令，最后才是代码去重构优化**。

### 建议 A（最高价值）：补齐 水管/管道 Pipe 全家 + 统一三专业 MEP Util（M3 提前并入）

管线综合在 Revit 中通常涵盖 给排水 / 暖通水系统 / 消防水管，目前桥内**完全没有 Pipe 支持**，是功能地图上最大空白。补齐 Pipe 的同时，把原本属于 M3 的三专业 Util 收敛**提前并进 M1**：**一次抽共享核心，三专业各留符合 SOLID 的薄门面**，避免再造第三份重复。

**① 共享核心（跨 风/电/水，一次性）** — 目录 `bridge/.../CliBridge/Handlers/Mep/`：
- `MepCurveGeometry.cs` — 单职责：MEP 曲线几何。`FindClosestConnectors` / `FindClosestConnector` / `ComputeIntersection` / `GetDirection` / `AreConnectorsCollinear` / `AngleBetweenDegrees` / `ValidateElbowPair` / `ValidateCollinearPair` + 常量（MinSegmentLength、PerpendicularityTolerance、CollinearDot）。入参一律 `MEPCurve`/`Connector`/`Curve`，与专业无关。
- `MepFittingResolver.cs` — 单职责：管件连接器解析。合并现行 `DuctFittingHelper` / `FittingHelper` 为一份，入参 `MEPCurve`，错误文案按专业动态拼接（"duct/cable tray/pipe"）。

**② 三专业薄门面（保留 Util，改造为符合 SOLID）** — 各自只保留本专业相关的类型解析/尺寸/快照，几何与连接器逻辑全部委托共享核心：
- 接口（接口隔离 ISP + 依赖倒置 DIP）：`IMepTypeResolver { Element? ResolveType(Document, int?) }`、`IMepSizeReader { MepSize ReadSize(MEPCurve) }`、`IMepSnapshotter { object Snapshot(MEPCurve, Document) }`。
- `DuctUtils` / `CableTrayUtils` / `PipeUtils`(新增) 由 `internal static` 改为单例注册实例并**实现上述接口**（SRP：每个 Xxx 只懂自己专业的类型/尺寸/快照；LSP：同接口可替换）。
- 开新专业（如 Conduit）= 新增实现类即可，**不碰任何现有 handler**（开闭 OCP）。
- 迁移风险控制：不改任何命令名/入参；现有 handler 调用点从静态改走接口注入；命令名/入参不变 + 既有单测全绿即视为安全。

Pipe 命令清单（目录 `bridge/.../CliBridge/Handlers/Piping/`）：
- `CreatePipeHandler.cs` → `create_pipe`（`start/end x/y/z`、`level_id`、`system_type_id`、`pipe_type_id`、`diameter_mm`）`SupportsDryRun`
- `GetPipesHandler.cs` → `get_pipes`（`PaginatedQueryHandler`，过滤 `level_id`、`system_type_id`、`diameter_mm`）
- `GetPipeTypesHandler.cs` → `get_pipe_types`
- `GetPipeSystemTypesHandler.cs` → `get_pipe_system_types`（`system_class` 可选过滤）
- `ModifyPipeHandler.cs` → `modify_pipe`（`element_id`、起讫点、`pipe_type_id`、`diameter_mm`、`offset`）
- 管件 6 个：`create_pipe_elbow_fitting` / `_tee_` / `_cross_` / `_transition_` / `_union_` / `_takeoff_`
- `create_mep_fitting`（统一入口，**提前交付**）：`--type elbow|tee|cross|transition|union|takeoff --class duct|pipe|cable_tray` + `element_id_1`/`element_id_2`/`connector_index_1`/`connector_index_2`

### 建议 B（差异化价值，管线综合核心）：分析类命令

区别于"通用创建"，管线综合的灵魂是**碰撞检查 + 净高分析**，值得单独投入。

- 目录：`bridge/.../CliBridge/Handlers/Coordination/`
- `CheckClashesHandler.cs` → `check_clashes`
  - 输入：`--ids`（int[]，缺省=按 `--categories` 或整文档机电元素）、`--categories`（如 `Pipes,Ducts,CableTrays`）、`--level-id`、`--tolerance-mm`（间距容差，捕捉近碰）、`--dry-run`
  - 算法：先用 `Element.BoundingBox` 快速排除（AABB），对候选对做**曲线/几何精确求交**；对平行近距段计算 `min distance < tolerance` 报为"近碰"
  - 输出：`[ {a_id, b_id, a_category, b_category, intersection_type: crossing|crossing.same_level|near_miss, point{x,y,z} 或 min_distance}]`
  - 纯逻辑（AABB + 曲线距离）抽成 `ClashDetector` 便于单测
- `CheckHeadroomHandler.cs` → `check_headroom`
  - 输入：`--plane-z`（mm，净空基准面，如楼层净高线/吊顶底）、`--ids` 或 `--categories`、`--min-headroom-mm`
  - 算法：取各元素几何底部 Z（对 MEP 曲线取 `LocationCurve` 的 z 减半径/半高，桥架取底标高），汇总穿过基准面的元素按底部标高升序
  - 输出：`[ {id, category, name, system_type_name, bottom_z_mm, headroom_mm, ok}]` + `summary {governing_id, governing_headroom_mm, pass}`
  - `HeadroomAnalyzer` 抽纯逻辑便于单测

### 建议 C（原第三阶段架构优化）→ 已并入 M1（见建议 A）

三专业 Util 收敛（`MepCurveGeometry`/`MepFittingResolver`/SOLID 门面）与 `create_mep_fitting` 统一管件入口均**前移进 M1**。M3 里程碑置空，不再单独排期。命令行兼容策略不变：保留 `create_duct_*` / `create_cable_tray_*` / `create_pipe_*` 名与入参，`create_mep_fitting` 仅作 Agent 更易发现的统一入口（读 schema/llms 时薄包装别名仍在）。

### 建议 D（小项，顺带处理）

- 命名与分类：分析命令建议 `Category` 用新值 `"Coordination"`；Go 端 `MapCategory` 对未知字符串回落 `"Custom"`（不阻塞），但为 CLI 帮助文本清晰，建议在 `client/internal/abstractions/categories.go` 补一个 `case "coordination"` 映射（一行改动）。
- 命令描写/参数集务必写完整（Description/Parameters/Examples），因为 SKILL.md 与 `llms.txt` 从 schema 自动生成，Agent 可发现性直接依赖它。
- 文档同步：`README.md`（Run Commands 段）、`bridge/README.md` 补充新命令示例。

---

## 2. 里程碑切分

| 里程碑 | 内容 | 交付物 |
|--------|------|--------|
| **M1 — Pipe 能力补齐 + 三专业 Util 收敛（含原 M3）** | 建议 A 全量：共享核心（`MepCurveGeometry`/`MepFittingResolver`）+ 三专业 SOLID 薄门面（Duct/CableTray 改造 + `PipeUtils` 新增）+ `create/get/types/system_types/modify` + 6 管件 + `create_mep_fitting`；配套单测（ParamBinder、connector 解析、Snapshot 单位） | 一条龙给排水/暖通水管建模能力，且一次消除三专业 Util 重复 |
| **M2 — 管线分析** | 建议 B：`check_clashes` + `check_headroom` + `ClashDetector`/`HeadroomAnalyzer` 单测；Go 侧补 Coordination 分类映射 | 碰撞检查 + 净高/净空分析（管线综合核心） |

---

## 3. 待确认问题

1. **范围**：✅ 已确认 — **M1（含原 M3 的 Util 收敛）+ M2**。M2 的分析命令仍偏"分析"而非"创建"，M1 交付后评估是否同步推进（当前计划为连做）。
2. **Conduit（线管）**：管线综合常含强电线管；是否也要纳入（会再 +6 管件）。建议先不做；因开闭 OCP 设计，后续新增仅需实现 `IMepTypeResolver`/`IMepSizeReader`/`IMepSnapshotter` + 6 薄包装 handler，不碰现有代码。
3. **碰撞检查语义**：`check_clashes` 首版定为 **几何/曲线级**（可单测、够用），不依赖 Revit 内部 `FailuresPrevention` 的硬件规则解析；是否满意，还是希望结合系统/管径做"净高优先"排序。
4. **净空基准**：`check_headroom` 的 `--plane-z` 首版让调用方传绝对标高（mm），是否需要一个 `--level-id + --clear-height` 的便捷模式。

---

## 4. 实施状态（MEP Util 三专业统一 — 共享核心 + 薄门面）

> 记录于 `feature/mep-util-unification` 分支。范围：建议 A 中"共享核心 + 三专业薄门面"部分（M1 前期阶段）。

### 4.1 已完成

共享核心（`bridge/RevitCliBridge/CliBridge/Handlers/Mep/`）：
- `MepCurveGeometry.cs`（新增）— 单职责几何/连接器逻辑：`FindClosestConnectors` / `FindClosestConnector` / `ComputeIntersection` / `GetDirection` / `AreConnectorsCollinear` / `AngleBetweenDegrees` / `ValidateElbowPair` / `ValidateCollinearPair` + 常量（`MinSegmentLengthFeet` / `PerpendicularityToleranceDeg` / `CollinearDotTolerance`）。
- `MepFittingResolver.cs`（新增）— `ResolveConnectorPair<T>(…)` 统一管件连接器解析，合并原 `DuctFittingHelper` / `FittingHelper`。
- `IMepCapabilities.cs`（新增）— 接口 `IMepTypeResolver`（`Element? ResolveType(Document,int?)`）+ `IMepSnapshotter`（`object Snapshot(MEPCurve,Document)`），接口隔离 + 依赖倒置。

三专业薄门面（仅保留本专业类型解析 / 尺寸 / 快照，几何与连接器全部委托共享核心）：
- `PipeUtils.cs`（`Handlers/Piping/`，新增）— 实现 `IMepTypeResolver` / `IMepSnapshotter`，含 `ResolveType` / `ResolveSystemType` / `GetDiameterMm` / `Snapshot`。
- `DuctUtils.cs`（`Handlers/Mechanical/`）— 由静态类改为单例 `DuctUtils.Default`，移除全部几何/连接器方法，实现接口，仅保留 `ResolveType` / `ResolveSystemType` / `GetSize` / `Snapshot`。
- `CableTrayUtils.cs`（`Handlers/Electrical/`）— 同上，保留 `ResolveType` / `GetSize` / `Snapshot`。

调用点迁移（命令名 / 入参一律不变）：
- 全部 Mechanical / Electrical 管件 handler（elbow / tee / cross / transition / union）改走 `MepCurveGeometry.*` 与 `MepFittingResolver.ResolveConnectorPair<T>`；
- `create_duct` / `create_cable_tray` / `get_ducts` / `get_cable_trays` / `modify_duct` / `modify_cable_tray` 的类型解析与快照改走 `XxxUtils.Default`。

### 4.2 待办（M1 其余）

- Pipe 命令 handler：`create_pipe`、`get_pipes`、`get_pipe_types`、`get_pipe_system_types`、`modify_pipe` + 6 管件 + `create_mep_fitting`。
- 配套单测：ParamBinder、connector 解析（`MepFittingResolver` / `MepCurveGeometry`）、Snapshot 单位（mm）。

### 4.3 兼容性与验证

- 共享核心与薄门面一律走 `.MillimeterToFeet()` / `.FeetToMillimeter()` 扩展，零改动跨 R19–R22（单位换算差异已集中在 `ConversionUtilities`，用 `#if (R19||R20)/(R21||R22)` 包裹）。
- macOS 本地无法编译 Revit 工程（依赖 Windows + Revit API）；编译绿依赖 CI（Windows Runner，`R19–R22` 各配置）验证。