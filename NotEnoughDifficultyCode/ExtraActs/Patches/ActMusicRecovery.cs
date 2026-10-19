using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     幕音乐"加载失败"状态的**清道夫** —— 让某一幕缺 bank 的后果不再污染整局。
///
/// ## 上一版（`ff28d61` 的 `ActBankMissingFallbackPatch`）为什么没修好（2026-10-19 实测）
/// 那版在 <c>LoadActBank</c> 前缀里"bank 缺失就换成别的基础幕 bank"。日志证明这条路走不通
/// （下面三行是 godot.log 原文引用，非代码中的路径）：
/// <code>
/// [音乐兜底] bank 缺失 'res://banks/desktop/Act4Heart/Act4Heart.bank' ⇒ 改用基础幕 'res://banks/desktop/act1_a1.bank'   // hardcode-ok: 日志原文引用
/// cannot find music path: event:/music/STS_Act4_BGM_v2                                                                   // hardcode-ok: 日志原文引用
/// [ERROR] Act music bank failed to load after 3 attempts; music will not follow combat state. path=res://banks/desktop/act1_a1.bank   // hardcode-ok: 日志原文引用
/// </code>
/// 两处都失败了：
/// <list type="number">
///   <item>只换了 **bank**，没换**曲子**：调用方仍要播那一幕自己的曲子（上面第二行），
///         它只存在于缺失的 Act4Heart bank 里 ⇒ 依然找不到；</item>
///   <item>换上去的基础幕 bank 又被引擎拒了 3 次（上面第三行）⇒ 说明"跨幕换 bank"这条路本身不成立
///         （引擎/代理会按当前幕校验）。</item>
/// </list>
/// ⇒ 已**删除换 bank** 的做法（不再引入新问题）。
///
/// ## 现在的做法：不让失败状态活到下一幕
/// 原版在某幕 bank 加载失败后会置一个"失败曲子"记号（<c>NRunMusicController._failedTrack</c>，
/// 名字取自游戏程序集转储）并停止跟随战斗状态 ⇒ **整局**没音乐、后面的幕（含本模组 act4/act5）
/// 也一起哑。每次建图（= 换幕）时把这个记号清掉 ⇒ 那一幕照样没音乐（bank 真的缺，无解），
/// 但**下一幕会重新尝试自己的 bank** ⇒ 本模组那两幕的音乐恢复。
///
/// 边界：清不掉只 Warn，不抛（这条路径在换幕时跑，绝不能让它拦住进幕）。
/// </summary>
internal static class ActMusicRecovery
{
    /// <summary>原版记录"加载失败的曲子"的私有字段名（取自游戏程序集转储）。</summary>
    private const string FailedTrackFieldName = "_failedTrack";

    private static readonly FieldInfo? FailedTrackField =
        AccessTools.Field(typeof(NRunMusicController), FailedTrackFieldName);

    /// <summary>换幕（每次 <c>NMapScreen.SetMap</c>）时调用：清掉失败记号，让新的一幕重新尝试加载。</summary>
    public static void ClearFailedTrack()
    {
        try
        {
            if (FailedTrackField == null) return;      // 版本变了就什么都不做（不抛）

            var instance = NRunMusicController.Instance;
            if (instance == null) return;

            var failed = FailedTrackField.GetValue(instance) as string;
            if (string.IsNullOrEmpty(failed)) return;  // 没有失败状态：不介入

            FailedTrackField.SetValue(instance, string.Empty);
            MainFile.Logger.Info(
                $"[音乐恢复] 换幕时清掉失败的曲子记号 '{failed}'" +
                "（那一幕 bank 真的缺失时仍无声，但下一幕会重新加载自己的 bank）");
        }
        catch (Exception ex)
        {
            MainFile.Logger.Warn($"[音乐恢复] 清理失败记号失败（不影响进幕）: {ex.Message}");
        }
    }
}
