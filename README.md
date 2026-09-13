# StS2-NotEnoughDifficulty（还不够难！）· **beta 移植分支**

> ### 分支身份
> - **上游（本 mod 的原始设计与实现）**：<https://github.com/bwnotfound/StS2-NotEnoughDifficulty>
> - **本分支**：`beta-0.111-port` —— 把 mod 整体迁移适配到《Slay the Spire 2》**public beta v0.111.0**
>   （MegaDot / Godot 4.5.1）+ **BaseLib 3.4.7**，并在此基础上做了成体系的扩展与兼容性改造。
> - **移植**：[@Coll-ed](https://github.com/Coll-ed)，**受原作者委托**完成。
> - **著作权**：本 mod 的设计与原始实现归原作者；本分支只包含移植适配与扩展部分，沿用上游 `LICENSE`。
>   发布/分发请以原仓库为准，或先与原作者确认。

把原版 3 层塔扩成 **5（+1）层**，并按层精细调节难度、地图与敌人池。单人 / 多人联机均可，
多人下 host 端配置自动同步给所有玩家。

本分支是**认真做的一版**：第 4/5 幕是重新设计的，视觉全部**程序化生成**（不新增任何第三方素材），
并且把"与其它模组共存"当成第一等需求来写。踩过的坑与结论都留在仓库里
（[`杀戮尖塔2-mod写作踩坑指南.md`](杀戮尖塔2-mod写作踩坑指南.md)，37 条 + §3.x 专题）。

---

## 目录

- [这个分支是什么](#这个分支是什么)
- [本分支做了什么](#本分支做了什么)
  - [1. 第 4 幕：精英连战（确定名单 · 不重复）](#1-第-4-幕精英连战确定名单--不重复)
  - [2. 第 5 幕：传奇 / 神话两档连战](#2-第-5-幕传奇--神话两档连战)
  - [3. 双 boss（按层开关）](#3-双-boss按层开关)
  - [4. 视觉：按"来源幕"的战斗背景与地图纹路](#4-视觉按来源幕的战斗背景与地图纹路)
  - [5. 多模组共存（本分支的重点）](#5-多模组共存本分支的重点)
  - [6. 保留并继续维护的上游功能](#6-保留并继续维护的上游功能)
- [配置项一览](#配置项一览)
- [构建（离线 · 自包含）](#构建离线--自包含)
- [安装](#安装)
- [怎么验证（日志关键字）](#怎么验证日志关键字)
- [已知限制](#已知限制)
- [版本历史（本分支）](#版本历史本分支)
- [文档](#文档)
- [致谢](#致谢)

---

## 这个分支是什么

上游把 mod 做到了 **1.0.1（BaseLib 3.3.0）**。游戏进入 beta v0.111.0 后，本分支做了三件事：

1. **移植**：全部代码在新游戏版本 + BaseLib 3.4.7 上跑通（构建自包含、零网络依赖）；
2. **重做第 4/5 幕**：目标不再是"随便加两层"，而是"这两层本身就是一个完整、可预期、不重复的玩法"；
3. **把多模组共存做成硬指标**：层号判定、名单口径、背景与场景，全部改成"按数据 / 按坐标"，
   而不是"按具体 act 类型"。

---

## 本分支做了什么

### 1. 第 4 幕：精英连战（确定名单 · 不重复）

**设计**：这一层就是"把游戏里所有基础精英各打一遍"，与"你之前打没打过"无关。

| 环节 | 做法 |
|---|---|
| 名单 | 全部基础精英（beta v0.111.0 为 **12 个**：密林 3 + 暗港 3 + 巢穴 3 + 荣耀 3），全局去重、顺序固定 |
| 落位 | 地图生成后按「离起点由近到远」（BFS 深度 → 列）**逐个钉**到战斗房上：`Dictionary<MapCoord, Encounter>` |
| 取怪 | 进房时按 **当前坐标查表**（`RunState.CurrentMapCoord`）⇒ **确定的、不可能重复** |
| 深度 | 从最低深度逐格加深，直到战斗房 ≥ 名单长度；富余战斗位→火堆，不足则问号→精英 |
| 图标 | 本层所有战斗房统一精英图标（数量 = 名单长度，不虚高） |

相关文件：`ExtraActs/Patches/Act4ElitePlan.cs`、`ActDepthPatch.cs`、`Act5/Act5MapPatch.cs`。

### 2. 第 5 幕：传奇 / 神话两档连战

**两档**（配置项 `Act5Difficulty`）：

- **考验（Trial）**：先古之名 → 一层随机 BOSS → 二层随机 BOSS → 最终 BOSS（三层当前分配的那个），中间不夹火堆。
- **神话（Extreme）**：一~二~三层循环取"**进入本幕之前没打过**"的 BOSS，三层那个压轴当最终 BOSS，
  **火堆数 = 场数 − 1**（每两场之间一个）。

**实现要点**：

- **自研地图节点类** `Act5BossNode : NMapPoint`（不是套用原版节点）：
  图标**跟着自己的 encounter 走**（spine 骨架优先，否则 `*_icon.png` / `*_icon_outline.png`），
  点击判定只认原始状态机 `State == Travelable`。
- **直线地图** `Act5LinearMap`：起点(先古之名) → 链位（伪装BOSS/火堆交替）→ `BossMapPoint`(最终BOSS)。
- **伪装 BOSS**：图标是 BOSS 图标、战斗是 BOSS 的 encounter，但**按小怪房结算**（小怪级奖励、不结束本幕）。
- **收尾闸门**：**只有踩在最终 BOSS 节点上的那一场**才结束本幕 —— 判据是**坐标**，与名单无关，永不漂移。
- **BOSS 图标改色**（程序化）：传奇 = 黑金；神话 = 血色。
- 节点多了会自动换布局（直线 ≤3 / 分叉 4–6 / 蛇形 ≥7）并等比缩放，不会跑出地图。

相关文件：`Act5/Act5LinearMap.cs`、`Act5/Act5MidBoss.cs`、`Act5/Act5BossNode.cs`、
`Act5/Act5BossDisplay.cs`、`Act5/Act5Disguised*Patch.cs`。

### 3. 双 boss（按层开关）

- 1~4 层各自一个开关（`Act1_DoubleBoss` … `Act4_DoubleBoss`），默认关。
- 第二个 boss 从**该层自己的 boss 池**里选（不含首个），用 **FNV-1a 稳定散列**抽取：
  不吃游戏随机流、host/client 各自算都一致。
- 两个 boss 之间可插入**合成火堆 / 商店**（`InterBossHearth` / `InterBossShop`），
  手法照搬工坊模组 Boss Gauntlet（见 [`BossGauntlet-合成节点法-拆解笔记.md`](BossGauntlet-合成节点法-拆解笔记.md)）。
- 进阶 10（N10）的双 boss 被**钉回第 3 层**（不随 act 列表变长而漂到最后一层）。

### 4. 视觉：按"来源幕"的战斗背景与地图纹路

- **战斗背景**：第 4/5 幕的**精英**战，背景切到"**敌人自己所属的那一幕**"。
  做法不是"自己造背景"，而是 **改写 `parentAct`**，让原版与第三方模组的背景钩子**照常生效** ——
  这样别的模组（如 ActsFromThePast 的自绘竞技场）也能正确显示。
- **地图纹路**：按来源幕变色。色相不是取 UI 的近黑色，而是从**该 act 自己的地图美术**统计出来的
  "招牌色相"；踩问号/商店/宝箱/火堆回默认紫。
- **第 5 幕主题底图**（程序化）：传奇 = 羊皮纸 + 每局随机的金色纹路；神话 = 血染（浓淡由噪声决定）。
- 全程**不引入任何外部素材**。

### 5. 多模组共存（本分支的重点）

| 场景 | 处理 |
|---|---|
| 别的模组给某层加了 **act 变体** | 层号按 `ModelDb.ActsByIndex` / `RunState.Acts` / `act.Index` **数据判定**（绝不按类型写死），整幕模组的 act 自动纳入池子 |
| 别的模组**占了第 4 幕**（如 ACT 4 心脏） | 本模组的两个幕**自动顺延为第 5、6 幕**，并保证排在 act 列表末尾；开关仍作用于自己的幕（`Act4HeartAutoShift`，可关） |
| 别的模组加了**精英/BOSS** | 自动进第 4 幕名单 / 第 5 幕编排（走各 act 自己的池子） |
| 别的模组**改了层号/顺序** | 名单按"进入本幕之前的战绩"计算 ⇒ 整幕内恒定，不漂移 |
| 别的模组**自己画背景** | 我们只改写 `parentAct`，它的钩子自己跑（不与它抢） |
| 别的模组**也想画地图节点** | 集成节点类只替换自己的节点；连锁/可通行性交给原版 `RecalculateTravelability` |

### 6. 保留并继续维护的上游功能

按层 HP / 伤害倍率（`1 + ActFloor × 0.1 × Y%` / `× 0.05 × X%`）、各层"额外强化"开关、难度预设、
地图长度与房间密度、敌人移除列表（含层后缀）、额外加速（引擎级 TimeScale，纯本地）、
ack-based 多人配置同步、读档健壮性兜底（mod act 数据丢失时按池确定性重建）。

---

## 配置项一览

| 分区 | 内容 |
|---|---|
| General | 总开关、日志调试（排障时打开，会把关键决策打成日志） |
| Difficulty | 全局 HP / 伤害倍率系数 X/Y |
| ExtraScalingPerAct | 1~5 各层的额外强化开关 |
| ActComposition | 各层是否双 boss、双 boss 之间插火堆 / 商店 |
| Act4_EncWeights / EventWeights / BossWeights | 第 4 幕的池子权重混合 |
| Act5Map | 第 5 幕是否用自定义直线地图、难度档位（考验 / 神话） |
| Compat | 第 4 幕被占用时自动顺延；自带背景的敌人是否强制按来源幕覆盖 |
| BehaviorToggles / Speed | 行为开关、额外加速倍率 |

配置项中英双语齐全（`localization/{zhs,eng}/settings_ui.json`）。

---

## 构建（离线 · 自包含）

```powershell
# 1) 依赖（本仓库不含，需自备）
#    <仓库根>\_refs\game\      ← 游戏目录 data_sts2_windows_x86_64 下的 sts2.dll / 0Harmony.dll /
#                                 GodotSharp.dll / MonoMod.*.dll / sts2.*.json
#    <仓库根>\_refs\baselib\   ← BaseLib.dll（工坊或作者发布页）
#    <仓库根>\_refs\nuget-feed\ ← Godot 4.5.1 的 4 个 nupkg（Godot.NET.Sdk / GodotSharp /
#                                 GodotSharpEditor / Godot.SourceGenerators）
# 2) 编译
dotnet build --no-incremental          # 产物 → .\dist\
# 3) 打包 .pck（本地化等资源；免装 Godot）
dotnet run --project tools\MakePck -- .\dist\NotEnoughDifficulty.pck NotEnoughDifficulty .\NotEnoughDifficulty (Get-ChildItem .\NotEnoughDifficulty\localization -Recurse -Filter *.json | % FullName)
```

> ⚠️ 与上游构建的差异：本分支的 `NotEnoughDifficulty.csproj` 用 `..\_refs\...` 本地引用 +
> `nuget.config` 指向本地离线 feed（为了在无网络/沙箱环境下也能构建）。合入上游前可能需要换回
> 上游原来的引用方式 —— 详见 [`README-移植与构建.md`](README-移植与构建.md)。

---

## 安装

把 `NotEnoughDifficulty.{dll,pdb,pck,json}` 一起放进
`<StS2>\mods\NotEnoughDifficulty\`，重启游戏。日志：`%APPDATA%\SlayTheSpire2\logs\godot.log`。
多人：所有玩家 mod 版本必须**严格一致**。

---

## 怎么验证（日志关键字）

打开 `General → 日志调试`，然后按关键字搜日志：

| 关心什么 | 关键字 |
|---|---|
| 第 4 幕名单与落位 | `[Act4] 精英落位:` / `[Act4] 战斗房 (col,row) → 指定精英` |
| 第 4 幕深度求解 | `[ActDepth] 收敛: 深度 … 战斗房 … ≥ 目标 …` |
| 第 5 幕编排与链位 | `[Act5] BOSS 编排（考验/极限）:` / `[Act5LinearMap] 建图: … 链位 N（伪装BOSS X + 火堆 Y）` |
| 第 5 幕节点注入 | `[Act5Mid] 注入完成: 链位 …` / `[Act5BossNode] (col,row) 就绪` |
| 不结束本幕是否生效 | `[Act5] 伪装BOSS 奖励界面「继续」→ 不结束本幕…` |
| 战斗背景跟随来源幕 | `[背景折返] '…'：parentAct … → '…'`（每场一行） |
| 地图纹路按来源幕 | `[Theme] '…' 招牌色相 = …°` / `[Stripe] 地图纹路 → 层 N，色相 …°` |
| 双 boss | `[DoubleBoss] 本局 act 分层判定：…` / `[DoubleBoss] actN: 首个='X' 第二='Y'` |
| 与心脏模组共存 | `[ActLayout] 检测到第 4 幕已被 '…' 占用 ⇒ 本模组自动顺延为第 5/6 幕` |

---

## 已知限制

- **`.pck` 需要自研打包器**：本分支不用 Godot 导出，`.pck` 由 `tools/MakePck` 生成（改本地化后必须重打）。
- **旧存档**：本分支某些修复只对"之后新建的地图"生效（例如第 5 幕火堆数量）；旧存档里已经建好的那一幕
  不会补，需要重进本幕或新开一局。
- **英文 README 尚未重写**：`docs/README.en.md` 仍是上游版本，内容落后于本分支。
- **多人**：不存在协议层向后兼容，升级需所有玩家同步升级。
- **多 mod 环境**：极端组合下仍可能有别的模组在 `FromSerializable` 链上丢数据导致读档异常；
  本 mod 已加防御兜底避免硬崩（`ExtraActs/Compat/RoomSetLoadNullGuardPatch.cs`），遇到请附日志反馈。

---

## 版本历史（本分支）

| 版本 / 提交 | 主要变化 |
|---|---|
| `beta-0.111-port` | 移植到 beta v0.111.0 + BaseLib 3.4.7；**第 4 幕重做为"确定名单"**（12 个精英各一次、按坐标落位、不重复）；**第 5 幕重做**（自研节点类 + 直线地图 + 传奇/神话两档 + 火堆 = 场数 − 1 + 只有最终 BOSS 收尾）；**战斗背景按来源幕**（改写 `parentAct`，兼容第三方）；**地图纹路按来源幕换色**（招牌色相从美术统计）；**多模组共存**（层号数据判定、第 4 幕被占自动顺延 5/6、双 boss 支持模组 act 变体）；修复移植过程中的 11 类实机问题（详见下方文档） |

---

## 文档

| 文档 | 内容 |
|---|---|
| [`杀戮尖塔2-mod写作踩坑指南.md`](杀戮尖塔2-mod写作踩坑指南.md) | **37 条实战坑** + 12 章方法论：读 IL 而不是猜、Harmony 属性 patch、BaseLib 钩子、时序、递归与日志洪泛、按类别收窄作用域…… |
| [`待办与交接-第三阶段.md`](待办与交接-第三阶段.md) | 当前进度、待办、硬事实（本分支的"交接入口"） |
| [`BossGauntlet-合成节点法-拆解笔记.md`](BossGauntlet-合成节点法-拆解笔记.md) | 在地图里插自定义节点（合成火堆/商店）的完整手法 |
| [`README-移植与构建.md`](README-移植与构建.md) | 移植差异、离线构建步骤、依赖清单 |

---

## 致谢

**本体**

- **[bwnotfound](https://github.com/bwnotfound)** —— 本 mod 的**设计与全部原始实现**（第 4/5 层、
  按层难度倍率、难度预设、地图长度/密度、敌人移除列表、额外加速、ack-based 多人配置同步、读档兜底…）。
  本分支是**受他委托**做的 beta 移植，**著作权归他**。

**扩展层与工具**

- [Alchyr](https://github.com/Alchyr) 的 [BaseLib](https://github.com/Alchyr/BaseLib-StS2) 与 [ModTemplate-StS2](https://github.com/Alchyr/ModTemplate-StS2)
  —— 自定义 act / 本地化登记 / 配置系统全靠 BaseLib 的官方钩子；
  "改不动就去 `BaseLib.Abstracts` 找钩子"是本移植贯穿始终的方法。
- [Harmony](https://github.com/pardeike/Harmony) —— 所有运行时改造的基础设施。
- [GlitchedReme](https://github.com/GlitchedReme) 的 [中文 STS2 modding 教程](https://github.com/GlitchedReme/SlayTheSpire2ModdingTutorials)
- 创意工坊模组 **Boss Gauntlet** 的作者 —— 合成节点（火堆/商店）的手法照搬自它。

**兼容性互操作（本分支新增的致谢）**

- **ritzukage** 的 **ActsFromThePast** 与 **RitsuLib** —— 它的 act / 精英 / BOSS 会被本 mod 第 4/5 层抽取；
  它的自绘战斗背景（`TheBeyondBackground`）促使本移植放弃"自己造背景"，改为改写 `parentAct`
  让**它自己的钩子照常生效**（踩坑指南 §3.17）。
- **YUI 系列扩展（YUI Spire / Card Expansion）**、**Act 4 Heart** 等模组 ——
  用于验证"与整幕模组共存"：本 mod 会在第 4 幕被别的模组占用时自动顺延为第 5/6 幕。
- **皮肤 / 卡图 / 汉化类模组** —— 用于验证本地化与设置界面注入不打架。

**本分支**

- [@Coll-ed](https://github.com/Coll-ed) —— beta v0.111.0 移植、第 4/5 幕重做、多模组共存改造、
  全部排查与文档。**不新增任何第三方素材**，视觉均为程序化生成。
