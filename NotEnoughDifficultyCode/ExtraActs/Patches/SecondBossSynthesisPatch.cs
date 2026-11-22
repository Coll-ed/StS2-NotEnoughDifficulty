using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     给**任何**地图补出"第二个 BOSS 点"——只要这一层确实要双 BOSS。
///
/// ## 为什么需要（2026-11-22 实测）
/// 双 BOSS 依赖 <c>ActMap.SecondBossMapPoint</c>：地图界面靠它建第二个 BOSS 节点、合成火堆靠它定位。
/// 但这个点只有 <see cref="StandardActMap" /> 在 ctor 里按 <c>hasSecondBoss</c> 建，别的地图类型没有：
/// <list type="bullet">
///   <item><c>GoldenPathActMap</c>（原版遗物<b>黄金罗盘</b>换的固定路线图）：ctor 只有 <c>(IRunState)</c>；
///         <c>ModifyGeneratedMap</c> IL：<c>if (GoldenPathAct == actIndex) return new GoldenPathActMap(runState);</c></item>
///   <item><c>SavedActMap</c>（读档恢复）：只恢复存档里有的点；若当时那张图没有第二 BOSS 点，恢复出来也没有。</item>
/// </list>
/// ⇒ 表现就是"这一层没有双 BOSS、火堆商店也不注入"。之前只能按设计跳过。
///
/// ## 为什么补点可行（原版 IL 实证）
/// <list type="bullet">
///   <item>原版公式：<c>BossMapPoint = (列数/2, 行数)</c>、<c>SecondBossMapPoint = (列数/2, 行数 + 1)</c>
///         —— 它本来就在**网格之外**，所以"网格里没有"不是问题；</item>
///   <item><c>ActMap.GetPoint(col, row)</c> 先比对 <c>BossMapPoint.coord</c>、再比对 <c>SecondBossMapPoint.coord</c>、
///         再比对起点，最后才查网格 ⇒ 补出来的虚拟坐标**能被解析**，原版进房流程（<c>RunManager.EnterMapCoord</c>）
///         不会在这里断；</item>
///   <item><c>SerializableActMap.SecondBossPoint</c> 存在 ⇒ 下一次存档会把补出来的点一起存下来（自愈）。</item>
/// </list>
///
/// ## 为什么三个 patch 类（不是一个）
/// <c>SecondBossMapPoint</c> 是**虚属性**：<see cref="StandardActMap" /> 与 <c>SavedActMap</c> 各自
/// override 出自己的 backing field，只有 <c>GoldenPathActMap</c> 这种不 override 的才走 <see cref="ActMap" /> 基类。
/// 虚调用不会回到基类 ⇒ 三处都得挂，否则"补点"只对其中一种地图生效。
/// </summary>
internal static class SecondBossSynthesis
{
    /// <summary>每张地图只补一次（同一张图的所有 getter 调用复用同一个点）。</summary>
    private static readonly ConditionalWeakTable<ActMap, MapPoint> Cache = new();

    internal static void Fill(ActMap? map, ref MapPoint __result)
    {
        if (map == null || __result != null) return;
        if (!PatchScope.IsEnabled) return;

        try
        {
            var act = RunStateAccessor.GetCurrentState()?.Act;
            if (act == null || !act.HasSecondBoss) return;   // 这层不要双 BOSS ⇒ 不动

            __result = Cache.GetValue(map, Build);
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[Gauntlet] 补第二个 BOSS 点失败（{map.GetType().Name}）: {ex.Message}");
        }
    }

    private static MapPoint Build(ActMap map)
    {
        var boss = map.BossMapPoint;
        var col = map.GetColumnCount() / 2;
        var row = map.GetRowCount() + 1;      // 与原版 StandardActMap ctor 同一公式

        var point = new MapPoint(col, row)
        {
            PointType = MapPointType.Boss,
            CanBeModified = false
        };

        if (boss != null)
        {
            boss.AddChildPoint(point);        // 连线 / 可通行性都沿 children 走
            point.parents.Add(boss);
        }

        MainFile.Logger.Warn(
            $"[Gauntlet] 这张地图（{map.GetType().Name}）没有第二个 BOSS 点，但本层要双 BOSS ⇒ " +
            $"按原版公式补一个虚拟点 ({col},{row})（黄金罗盘 / 读档恢复等换图场景）；下次存档会一并保存");

        return point;
    }
}

/// <summary>基类 getter：黄金罗盘那类"不 override 第二 BOSS 点"的地图走这里。</summary>
[HarmonyPatch(typeof(ActMap), nameof(ActMap.SecondBossMapPoint), MethodType.Getter)]
internal static class SecondBossOnAnyMapPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(ActMap __instance, ref MapPoint __result) =>
        SecondBossSynthesis.Fill(__instance, ref __result);
}

/// <summary>标准地图：正常建图时会自带该点，这里只兜"没带"的情况（例如别人重造的图）。</summary>
[HarmonyPatch(typeof(StandardActMap), nameof(StandardActMap.SecondBossMapPoint), MethodType.Getter)]
internal static class SecondBossOnStandardActMapPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(StandardActMap __instance, ref MapPoint __result) =>
        SecondBossSynthesis.Fill(__instance, ref __result);
}

/// <summary>读档恢复的地图：存档里没有第二 BOSS 点时补回来。</summary>
[HarmonyPatch(typeof(SavedActMap), nameof(SavedActMap.SecondBossMapPoint), MethodType.Getter)]
internal static class SecondBossOnSavedActMapPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Low)]
    private static void Postfix(SavedActMap __instance, ref MapPoint __result) =>
        SecondBossSynthesis.Fill(__instance, ref __result);
}
