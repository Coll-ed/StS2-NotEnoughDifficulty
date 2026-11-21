// ReSharper disable InconsistentNaming

using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
/// 修复进入 act4/5 篝火时 <c>NRestSiteCharacter._Ready</c> 抛 <c>InvalidOperationException("Unexpected act")</c>。
///
/// ## 问题
///
/// base game <c>NRestSiteCharacter._Ready</c>（src/Core/Nodes/RestSite/NRestSiteCharacter.cs:80）：
/// <code>
///   string animationName = Player.RunState.CurrentActIndex switch
///   {
///       0 => "overgrowth_loop", 1 => "hive_loop", 2 => "glory_loop",
///       _ => throw new InvalidOperationException("Unexpected act"),
///   };
/// </code>
///
/// <c>CurrentActIndex</c> 对 act4 是 3、act5 是 4 → 命中 <c>_ => throw</c>。篝火角色 _Ready 抛异常 →
/// 篝火房间初始化中断（连带依赖篝火 _Ready 流程的其它 mod 也失效）。
///
/// ## 修复思路 + 一个必须避开的陷阱
///
/// 思路是 _Ready 期间临时把 CurrentActIndex 映射到 base game 认识的值（act4→1 Hive、act5→2 Glory），
/// _Ready 结束后恢复。
///
/// ⚠️ <b>陷阱</b>：绝对不能走 <c>CurrentActIndex</c> 的 property setter。base game 的
/// <c>RunState.CurrentActIndex</c> setter 在值改变时会
/// <c>_visitedMapCoords.Clear()</c> + <c>ActFloor=0</c> + <c>NextRoomId=0</c>——
/// "临时改过去再改回来"会触发两次，把当前 act 的地图进度彻底清空，导致
/// <c>CurrentMapCoord</c> 变 null、玩家被送回 act 开始、选下一个地图节点时卡死。
///
/// 早期版本的本 patch 正是用 property setter 临时改值，引入了"篝火后回到层开始 + 选下一关卡住"
/// 的严重 bug。
///
/// 正确做法：用 <see cref="RunStateActIndexWriter.WriteRaw"/> 直接写底层 <c>_currentActIndex</c>
/// 字段，绕过 setter——纯改一个 int，不触发任何副作用。详见该类的文档。
///
/// ## 为什么 prefix/postfix 临时改值是安全的
///
/// <c>NRestSiteCharacter._Ready</c> 是<b>同步 void 方法</b>，且我完整看过它的实现——开头读一次
/// <c>CurrentActIndex</c> 选动画名，之后全是 <c>SetAnimation</c> / <c>Connect</c> 等 UI 操作，
/// 没有 await、没有存档、不触发别的节点 _Ready。所以 prefix → _Ready → postfix 之间
/// 没有任何"会读到被改的 CurrentActIndex 并产生持久后果"的代码。
///
/// ## 健壮性
///
/// - prefix 只在 <see cref="RunStateActIndexWriter.WriteRaw"/> 成功时才设 <c>__state</c>；
///   失败（找不到字段）则不改值、不记录——_Ready 会重新抛 "Unexpected act"，但<b>不会污染存档</b>，
///   这是有意的取舍：宁可篝火崩，也不破坏 run state。
/// - postfix 带 <c>Exception __exception</c> 参数——这样即使 _Ready 因任何原因抛异常，
///   Harmony 也会执行 postfix，<b>保证 CurrentActIndex 一定被恢复</b>。
///
/// ## Priority
///
/// prefix <see cref="Priority.First"/>、postfix <see cref="Priority.Last"/>：让中间所有其它 mod
/// 对 _Ready 的 patch 都看到映射后的合法值。
/// </summary>
[HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
public static class RestSiteCharacterCustomActPatch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    public static void Prefix(NRestSiteCharacter __instance, out int __state)
    {
        __state = -1; // sentinel：未改动 / 改动失败

        if (!PatchScope.IsEnabled) return;

        try
        {
            var runState = __instance?.Player?.RunState;
            if (runState == null) return;

            int idx = runState.CurrentActIndex; // getter 无副作用，直接读
            if (idx < 3) return;                // 只有 ≥3（本模组幕 / 别人的额外幕）才需要映射

            var mapped = MappedActIndexFor();
            if (mapped < 0)
            {
                MainFile.Logger.Warn(
                    $"RestSiteCharacter: CurrentActIndex={idx} 但认不出是哪个幕" +
                    $"（act={RunStateAccessor.GetCurrentState()?.Act?.Id?.Entry ?? "?"}）" +
                    " ⇒ 不改值，_Ready 可能抛 'Unexpected act'");
                return;
            }

            // 直接写底层字段，绕过 setter 的清地图副作用
            if (RunStateActIndexWriter.WriteRaw(runState, mapped))
            {
                __state = idx; // 只有写成功才记录原值，postfix 才会恢复
                MainFile.Logger.Info(
                    $"RestSiteCharacter: act{idx + 1} detected, temporarily mapping " +
                    $"CurrentActIndex {idx} -> {mapped} via raw field write " +
                    $"(bypasses the setter's map-clearing side effects)");
            }
            else
            {
                // WriteRaw 失败：放弃改值。_Ready 会抛 "Unexpected act"（篝火崩），
                // 但存档不会被污染。这是有意的安全取舍。
                MainFile.Logger.Warn(
                    "RestSiteCharacter: could not safely remap CurrentActIndex; " +
                    "rest site _Ready may throw, but the run state is left intact");
            }
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"RestSiteCharacterCustomActPatch.Prefix failed: {ex}");
        }
    }

    /// <summary>
    ///     当前这一"篝火角色"该读哪一幕的动画名（0=overgrowth_loop / 1=hive_loop / 2=glory_loop）。
    ///
    /// ## 为什么不再写死 "3→1 / 4→2"（2026-11-22 实测崩溃）
    /// `CurrentActIndex` 是**绝对**幕号：基础三幕 0~2，别人的整幕（如 THE_ENDING）会占 3，
    /// 于是本模组的精英幕可能落到 **4**、传奇幕落到 **5** —— 写死映射就会给出错误的动画名。
    /// 正确口径是**跟这一幕自己用的篝火美术走**：
    /// <list type="bullet">
    ///   <item><c>Act4Model.CustomRestSiteBackgroundPath</c> = <c>DominantSourceActModel()?.RestSiteBackgroundPath</c>
    ///         （读不到时退回 <c>hive_rest_site</c>）；</item>
    ///   <item><c>Act5Model</c> 固定 <c>glory_rest_site</c>。</item>
    /// </list>
    /// ⇒ act4 的动画名应当由 <see cref="RunProgress.GetDominantEliteSourceAct" />（主导来源层 1~3）决定，
    /// act5 固定 glory(2)。实测那一局主导来源 = act2 ⇒ 该播 <c>hive_loop</c>，
    /// 而旧代码给的是 <c>glory_loop</c>（错一幕）。
    /// </summary>
    private static int MappedActIndexFor()
    {
        try
        {
            var state = RunStateAccessor.GetCurrentState();
            if (state == null) return -1;

            if (state.Act is Act5Model) return 2;      // glory_rest_site

            if (state.Act is Act4Model)
            {
                var layer = RunProgress.GetDominantEliteSourceAct(state); // 1~3
                return layer is >= 1 and <= 3 ? layer - 1 : 1;            // 读不到时退回 hive
            }

            return -1;   // 别人的额外幕：不动它（原版会抛，让那一方自己处理）
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"RestSiteCharacter: MappedActIndexFor failed: {ex.Message}");
            return -1;
        }
    }

    [HarmonyPriority(Priority.Last)]
    [HarmonyPostfix]
    public static void Postfix(NRestSiteCharacter __instance, int __state, Exception __exception)
    {
        // 带 __exception 参数 → 即使 _Ready 抛异常，Harmony 也会调用本 postfix，
        // 保证临时改掉的 CurrentActIndex 一定被恢复。__exception 本身不需要处理。
        if (__state < 0) return; // prefix 未改动 / 改动失败，无需恢复

        try
        {
            var runState = __instance?.Player?.RunState;
            if (runState == null) return;

            // 同样用 raw field write 恢复，绕过 setter
            RunStateActIndexWriter.WriteRaw(runState, __state);

            // ★ 崩溃取证用：这行出现在日志里 = _Ready 这一趟**正常跑完了**；
            //   如果日志里只有 prefix 那行、没有这行，说明进程死在 _Ready 内部（原生崩溃，无托管堆栈）。
            MainFile.DebugLog(
                $"RestSiteCharacter: _Ready 结束，CurrentActIndex 已恢复为 {__state}" +
                $"{(__exception == null ? "" : $"（_Ready 期间抛过 {__exception.GetType().Name}）")}");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Error($"RestSiteCharacterCustomActPatch.Postfix failed: {ex}");
        }
    }
}