using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     黄金罗盘（<see cref="GoldenCompass" />）与"双 BOSS"的冲突修复。
///
/// ## 现象（用户 2026-11-22 实测）
/// 第 2 层（STS2_Things 的 <c>ThingsDepths</c>）有第二个 BOSS，但火堆/商店没注入：
/// <code>
/// 建图前检查: act=ThingsDepths idx=2 HasSecondBoss=True second=THINGS_CAVE_GOD_BOSS_ENCOUNTER
/// SetMap: map=GoldenPathActMap secondBoss=无          ← 地图被换成了黄金之路
/// 跳过火堆注入：第 2 层地图没有第二个 BOSS 点
/// </code>
///
/// ## 根因（IL 实证，不是别的模组）
/// <c>GoldenCompass.ModifyGeneratedMap(runState, map, actIndex)</c>：
/// <code>
///   IL_0000: call get_GoldenPathAct()
///   IL_0006: ldarg.3                 // actIndex
///   IL_0007: beq.s  IL_000b          // 相等 ⇒ 换图
///   IL_0009: ldarg.2                 // 不相等 ⇒ 原样返回 map
///   IL_000B: newobj GoldenPathActMap::.ctor(IRunState)   // ← 换成黄金之路
/// </code>
/// 而 <c>GoldenPathActMap</c> 是原版的一个**固定路线图**：ctor 只有 <c>(IRunState)</c> 一个参数，
/// **没有 hasSecondBoss**，也没有覆写 <c>SecondBossMapPoint</c>（继承 <c>ActMap</c> 的默认值 ⇒ 永远 null）。
/// ⇒ 持有黄金罗盘的那一层，双 BOSS 从数据结构上就不存在，我们的注入只能"正确地跳过"。
///
/// ## 修法
/// 这一层**确实要双 BOSS**（<c>act.HasSecondBoss == true</c>）时，postfix 把返回值换回原地图
/// （<c>StandardActMap</c>，带 <c>SecondBossMapPoint</c>）⇒ 双 BOSS + 合成火堆/商店恢复注入。
/// 不需要双 BOSS 的层完全不动罗盘行为；罗盘的 <c>ModifyUnknownMapPointRoomTypes</c> 也不碰
/// （它只影响问号房里能出什么，跟地图拓扑无关）。
///
/// 代价写得明明白白：那一层失去"黄金之路"的固定路线，换来双 BOSS。日志 Warn 会点名，不静默。
/// </summary>
[HarmonyPatch(typeof(GoldenCompass), nameof(GoldenCompass.ModifyGeneratedMap))]
internal static class GoldenPathDoubleBossPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]   // 让别的 mod 先看/先改，我们最后裁决
    private static void Postfix(IRunState runState, ActMap map, int actIndex, ref ActMap __result)
    {
        if (!PatchScope.IsEnabled) return;

        // 罗盘没换图（不是它的目标层）⇒ 什么都不用做
        if (__result is not GoldenPathActMap) return;

        ActModel? act = null;
        var acts = runState?.Acts;
        if (acts != null && actIndex >= 0 && actIndex < acts.Count) act = acts[actIndex];

        if (act == null)
        {
            MainFile.DebugLog(
                $"[Gauntlet] 黄金罗盘：取不到第 {actIndex + 1} 层的 act（acts={acts?.Count ?? -1}）⇒ 保留罗盘效果");
            return;
        }

        if (!act.HasSecondBoss)
        {
            MainFile.DebugLog(
                $"[Gauntlet] 黄金罗盘：第 {actIndex + 1} 层（{act.Id?.Entry}）没有双 BOSS ⇒ 保留罗盘效果");
            return;
        }

        __result = map;   // ← 退回标准地图：它有 SecondBossMapPoint，双 BOSS 才能成立
        MainFile.Logger.Warn(
            $"[Gauntlet] 第 {actIndex + 1} 层（{act.Id?.Entry}）配置了双 BOSS，但黄金罗盘会把它换成 " +
            "GoldenPathActMap（该地图类型没有第二个 BOSS 点）⇒ 本层退回标准地图，" +
            "双 BOSS 与合成火堆/商店恢复；其它层罗盘照常生效");
    }
}
