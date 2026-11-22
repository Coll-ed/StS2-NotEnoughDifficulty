using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     本模组 act4 / act5 的**篝火崩溃**：把原版两句 "切篝火环境音" 的 Native 调用挡掉。
///
/// ## 两次实机崩溃栈（Windows 事件日志 `.NET Runtime` 1026 原文，进程被未处理异常杀掉，
///    WER 同时记 `Application Error` 1000 / 异常代码 0xc0000005 = 访问越界，coreclr.dll）
///
/// ① 2026-11-22 16:20 **进火堆时**：
/// <code>
/// at Godot.NativeCalls.godot_icall_2_875(...)
/// at NRunMusicController.UpdateTrack_Patch1(NRunMusicController, RoomType)
/// at NRunMusicController.UpdateTrack_Patch2(NRunMusicController)
/// at RunManager+&lt;EnterRoomInternal&gt;d__229.MoveNext()
/// at RestSiteRoom+&lt;EnterInternal&gt;d__7.MoveNext()
/// </code>
///
/// ② 2026-11-22 16:42 **进火堆后选「敲牌」确认升级时**：
/// <code>
/// at Godot.NativeCalls.godot_icall_2_875(...)
/// at NRunMusicController.TriggerCampfireGoingOut_Patch2(NRunMusicController)
/// at NRestSiteRoom.ExtinguishFireIfAble()
/// at NRestSiteRoom+&lt;AfterSelectingOptionAsync&gt;d__60.MoveNext()
/// at SmithRestSiteOption+&lt;OnSelect&gt;d__8.MoveNext()
/// </code>
/// 两次都是**同一个 Native icall**、同一个进程死法。
///
/// ## 为什么是这两个方法（IL 实证，不是猜）
/// 整个 <c>NRunMusicController</c> 里只有**两处**调用 `_updateCampfireAmbienceCallback`：
/// <list type="number">
///   <item><c>UpdateTrack(RoomType)</c>：<c>if (CurrentRoom is RestSiteRoom) _proxy.Call(cb, 0)</c>（0 = 开）</item>
///   <item><c>TriggerCampfireGoingOut()</c>：<c>_proxy.Call(cb, 1)</c>（1 = 熄火淡出）</item>
/// </list>
/// 非篝火房间根本走不到这两句，所以 act4 的 BOSS / 精英 / 小怪房都正常 —— 与"只有火堆崩"完全吻合。
/// 另一条旁证：这两句要播的是**篝火环境音**，而本机 `res://banks/desktop/Act4Heart/Act4Heart.bank`
/// 加载失败（原版日志 `Act music bank failed to load after 3 attempts`）、
/// `res://music/STS_Act4_BGM_v2.ogg` 也 `Asset not cached` ⇒ 代理那边拿不到可播的事件。
///
/// ## 做法：只跳过这两句，别的一律不动
/// 条件统一为「当前幕是 <see cref="Act4Model" /> / <see cref="Act5Model" /> **且** 当前房间是篝火」。
/// 命中就 <c>return false</c>（原版整段不执行）；其它幕、其它房间类型原版照跑。
/// 每次命中打一行取证（含 `_proxy` 是否还活着），方便下一轮定位。
///
/// ## 边界（诚实记录）
/// <list type="bullet">
///   <item><b>不能用 try/catch 兜</b>：这是 AV，托管 try 抓不到，只能提前不调用；</item>
///   <item>代价：这一幕的篝火**没有环境音淡出/淡入**（act4 缺 bank，本来也没音乐）；</item>
///   <item>**没证到**的一环：代理内部到底是哪个对象/句柄失效 —— 代理是场景/GDScript 资产，
///         不在 sts2.dll 里，从托管侧看不到。所以这里做的是"不再踩这个坑"，不是"把坑填好"。</item>
/// </list>
/// </summary>
internal static class ActRestSiteCampfireGuard
{
    public const string UpdateTrackSite = "UpdateTrack(进火堆)";
    public const string GoingOutSite = "TriggerCampfireGoingOut(选完选项熄火)";

    private static readonly FieldInfo? ProxyField = AccessTools.Field(typeof(NRunMusicController), "_proxy");

    /// <summary>当前是不是"本模组的幕 + 篝火房间"。命中才拦。</summary>
    public static bool Applies(out IRunState? state)
    {
        state = null;
        if (!PatchScope.IsEnabled) return false;

        try
        {
            state = RunStateAccessor.GetCurrentState();
            if (state == null) return false;
            if (state.CurrentRoom is not RestSiteRoom) return false;
            return state.Act is Act4Model or Act5Model;
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[音乐守卫] 判断失败，交回原版: {ex.Message}");
            return false;
        }
    }

    /// <summary>打一行取证：代理是否还活着 + 本方法里被跳过的是哪一句。</summary>
    public static void Log(NRunMusicController controller, IRunState? state, string site)
    {
        string proxyState;
        try
        {
            var proxy = ProxyField?.GetValue(controller) as Godot.Node;
            proxyState = proxy == null
                ? "null"
                : Godot.GodotObject.IsInstanceValid(proxy) ? "valid" : "已释放";
        }
        catch
        {
            proxyState = "读不到";
        }

        MainFile.Logger.Info(
            $"[音乐守卫] {state?.Act?.GetType().Name ?? "?"} 篝火 · {site}：" +
            $"跳过原版 campfire ambience 调用（AV 崩溃点）；_proxy={proxyState}");
    }
}

/// <summary>入口①：进篝火房时 <c>EnterRoomInternal</c> 调的那次。</summary>
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), typeof(RoomType))]
internal static class ActRestSiteMusicTrackGuardPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NRunMusicController __instance)
    {
        if (!ActRestSiteCampfireGuard.Applies(out var state)) return true;

        ActRestSiteCampfireGuard.Log(__instance, state, ActRestSiteCampfireGuard.UpdateTrackSite);
        return false; // 原版整段不执行：Progress 与环境音两句 Call 都不发
    }
}

/// <summary>入口②：在篝火里选完选项（休息 / 敲牌 / 其它）后熄火淡出。</summary>
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.TriggerCampfireGoingOut))]
internal static class ActRestSiteMusicGoingOutGuardPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NRunMusicController __instance)
    {
        if (!ActRestSiteCampfireGuard.Applies(out var state)) return true;

        ActRestSiteCampfireGuard.Log(__instance, state, ActRestSiteCampfireGuard.GoingOutSite);
        return false; // 只跳过"环境音淡出"；篝火视觉/流程在 NRestSiteRoom 里，不受影响
    }
}
