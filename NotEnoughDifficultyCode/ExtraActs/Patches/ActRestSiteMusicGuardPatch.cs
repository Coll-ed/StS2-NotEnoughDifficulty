using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     本模组 act4 / act5 的**火堆进房崩溃**：把原版那句音乐/环境音切换挡掉。
///
/// ## 崩溃栈（2026-11-22 16:20 实机，Windows 事件日志 .NET Runtime 1026 原文）
/// <code>
/// at Godot.NativeCalls.godot_icall_2_875(...)
/// at Godot.GodotObject.Call_Patch1(GodotObject, StringName, Variant[])
/// at NRunMusicController.UpdateTrack_Patch1(NRunMusicController, RoomType)     // ← 就是这里
/// at NRunMusicController.UpdateTrack_Patch2(NRunMusicController)
/// at RunManager+&lt;EnterRoomInternal&gt;d__229.MoveNext()
/// at RestSiteRoom+&lt;EnterInternal&gt;d__7.MoveNext()
/// at PreloadManager+&lt;LoadRoomRestSite&gt;d__18.MoveNext()
/// </code>
/// 进程被"未处理异常"杀掉，WER 记录异常代码 0xc0000005（access violation，coreclr.dll）。
/// 也就是说：火堆房间本身起来了（我们 `RestSiteCharacter: _Ready 结束` 的取证行已经打出来了，
/// `YUIEXTRA` 的休息点钩子也跑完了），**死的是进房流程最后一步 `RunManager.EnterRoomInternal`
/// 里的 `NRunMusicController.UpdateTrack(RoomType)`** —— 它 `Call` 到 Native 时把进程带走，
/// 所以日志里既没有托管异常栈、也没有 `GodotShuttingDown` 收尾。
///
/// ## 为什么只在本模组的幕里炸
/// <c>UpdateTrack(RoomType)</c> 里只有两处 <c>GodotObject.Call</c>（IL 实证）：
/// <list type="number">
///   <item><c>UpdateTrack("Progress", (float)GetTrack(roomType))</c> —— **每个房间都走**，
///         act4 的 BOSS / 精英 / 小怪房都正常进 ⇒ 这一句是好的；</item>
///   <item><c>if (_runState.CurrentRoom is RestSiteRoom) _proxy.Call(_updateCampfireAmbienceCallback, 0)</c>
///         —— **只有火堆走**，而实测正是"只有火堆崩" ⇒ 凶手是这一句。</item>
/// </list>
/// 背景音 bank 也印证：`res://banks/desktop/Act4Heart/Act4Heart.bank` 在本机**加载失败**
/// （原版日志 `Act music bank failed to load after 3 attempts`），代理节点去播那一幕的
/// 篝火环境音时拿到的是空事件 ⇒ Native 侧炸。act4 本来就没有音乐，跳过它听觉上没有损失。
///
/// ## 做法（最小面）：只**跳过**，不改任何状态
/// 条件是"当前房间是篝火 **且** 当前幕是 <see cref="Act4Model" /> / <see cref="Act5Model" />"，
/// 命中就 return false（原版整段不执行），其它幕、其它房间类型原版照跑。
/// 代理节点（<c>_proxy</c>）是否还活着会打一行取证，方便下一轮定位。
///
/// ## 已知取舍
/// <list type="bullet">
///   <item>这一间篝火不会有"篝火环境音切换"（act4 缺 bank，本来也没有音乐）；</item>
///   <item>不能 `try/catch` 兜 —— AV 直接杀进程，托管 try 抓不到，所以只能提前不调用。</item>
/// </list>
/// </summary>
[HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), typeof(RoomType))]
internal static class ActRestSiteMusicGuardPatch
{
    /// <summary><c>NRunMusicController._proxy</c>：`_Ready` 里 `GetNode("Proxy")` 拿到的原生代理节点。</summary>
    private static readonly FieldInfo? ProxyField = AccessTools.Field(typeof(NRunMusicController), "_proxy");

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NRunMusicController __instance)
    {
        if (!PatchScope.IsEnabled) return true;

        try
        {
            var state = RunStateAccessor.GetCurrentState();
            if (state == null) return true;
            if (state.CurrentRoom is not RestSiteRoom) return true;            // 只有篝火才有那句 Call
            if (state.Act is not (Act4Model or Act5Model)) return true;        // 别人的幕 / 基础三幕：完全不动

            var proxy = ProxyField?.GetValue(__instance) as Godot.Node;
            var proxyState = proxy == null
                ? "null"
                : Godot.GodotObject.IsInstanceValid(proxy) ? "valid" : "已释放";

            MainFile.Logger.Info(
                $"[音乐守卫] {state.Act.GetType().Name} 篝火：跳过原版 UpdateTrack 的 campfire ambience 调用" +
                $"（2026-11-22 的 AV 崩溃点）；_proxy={proxyState}");

            return false; // 原版整段不执行：两句 Call 都不发
        }
        catch (Exception ex)
        {
            // 兜底失败就交回原版（宁可维持旧行为，也不要静默吞掉音乐逻辑）
            MainFile.Logger.Warn($"[音乐守卫] 判断失败，交回原版: {ex.Message}");
            return true;
        }
    }
}
