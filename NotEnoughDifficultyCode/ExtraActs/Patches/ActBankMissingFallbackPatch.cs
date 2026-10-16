using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     幕音乐 bank 缺失时的兜底 —— 让"某个 mod 的 bank 没打进包"不再把**整局音乐**弄哑。
///
/// ## 症状（2026-10-16 用户："4、5 层的音乐消失了"，下面四行是 godot.log 原文引用）
/// <code>
/// music bank not found: res://banks/desktop/Act4Heart/Act4Heart.bank          // hardcode-ok: 引用用户日志原文，非代码写死
/// [WARN] Act music bank load failed (attempt 1/3), retrying. path=res://banks/desktop/Act4Heart/Act4Heart.bank   // hardcode-ok: 同上
/// [WARN] Act music bank load failed (attempt 2/3), retrying. path=res://banks/desktop/Act4Heart/Act4Heart.bank   // hardcode-ok: 同上
/// [ERROR] Act music bank failed to load after 3 attempts; music will not follow combat state.
///         at MegaCrit.Sts2.Core.Nodes.Audio.NRunMusicController.UpdateMusic_Patch5
/// cannot find music path: event:/music/STS_Boss4_v6                          // hardcode-ok: 引用用户日志原文
/// </code>
/// ⇒ 第 4 幕（THE_ENDING / Act4Heart 模组）的 bank 文件不在它的包里，原版连试 3 次后放弃，
/// 并置"music will not follow combat state" —— 这是**整局**状态，于是紧接的第 5 幕（本模组 act4）
/// 也一起没音乐。⇒ 现象是"4、5 层都没音乐"，但根因是**一个缺失的 bank 文件**。
///
/// ## 修法（最小介入：只在 bank 不存在时换一对存在的）
/// 原版 <c>NRunMusicController.LoadActBank(bankPath, verifyEvent)</c> 是 private（元数据里可见），
/// 但 Harmony 可以按名字 patch（与我们在 NMapScreen.DrawPaths 上的做法一致）。
/// 在前缀里先 <c>FileAccess.FileExists(bankPath)</c> 判断：
/// <list type="bullet">
///   <item>存在 ⇒ 不介入，原样走；</item>
///   <item>不存在 ⇒ 把 (bankPath, verifyEvent) 换成一对**确定存在**的基础幕 bank + 配套 event，
///         让原方法正常加载成功 ⇒ 音乐继续跟随，不会整局失灵（音色会变成基础幕那一首）。</item>
/// </list>
///
/// ## 兜底来源（单一来源，不写死路径）
/// <see cref="RunProgress.GetRepresentativeAct" /> 取每层代表 act，用它的
/// <c>MusicBankPaths</c> 与 <c>BgMusicOptions</c> **按下标配对**（bank[i] ↔ option[i]，
/// 与原版 <c>ResolveMusic(options, bankPaths, seed)</c> 的配对口径一致），取第一个文件存在的。
/// </summary>
[HarmonyPatch(typeof(NRunMusicController), "LoadActBank")]   // 名字取自游戏程序集转储（private 方法）
public static class ActBankMissingFallbackPatch
{
    /// <summary>兜底时向上找几层（基础三幕）。具名常量：本模组只改了 1~5 层，基础幕就是 1~3。</summary>
    private const int FirstBaseLayer = 1;

    /// <summary>基础幕的最后一层（见 <see cref="FirstBaseLayer" /> 的理由）。</summary>
    private const int LastBaseLayer = 3;

    [HarmonyPriority(Priority.High)]   // 早于原方法体：原方法一开始就会去 load，必须提前换好
    [HarmonyPrefix]
    public static void Prefix(ref string bankPath, ref string verifyEvent)
    {
        if (!PatchScope.IsEnabled) return;
        if (string.IsNullOrEmpty(bankPath)) return;
        if (Godot.FileAccess.FileExists(bankPath)) return;        // 正常情况：不介入（Godot.FileAccess：避免与 System.IO.FileAccess 歧义）

        var pair = FindExistingBaseBank();
        if (pair == null)
        {
            MainFile.Logger.Warn(
                $"[音乐兜底] bank 缺失且找不到可用的基础幕 bank：{bankPath}" +
                "（本局音乐可能不再跟随战斗状态）");
            return;
        }

        var (bank, track) = pair.Value;
        MainFile.Logger.Warn(
            $"[音乐兜底] bank 缺失 '{bankPath}' ⇒ 改用基础幕 '{bank}'（verify '{track}'）" +
            "，避免原版连试 3 次失败后整局音乐失灵");
        bankPath = bank;
        verifyEvent = track;
    }

    /// <summary>找一个"文件确实存在"的基础幕 (bank, 配套 event)；找不到返回 null。</summary>
    private static (string Bank, string Track)? FindExistingBaseBank()
    {
        for (var layer = FirstBaseLayer; layer <= LastBaseLayer; layer++)
        {
            var act = RunProgress.GetRepresentativeAct(layer);
            if (act == null) continue;

            var banks = act.MusicBankPaths;
            var tracks = act.BgMusicOptions;
            if (banks == null || tracks == null) continue;

            var count = Math.Min(banks.Length, tracks.Length);
            for (var i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(banks[i]) || string.IsNullOrEmpty(tracks[i])) continue;
                if (Godot.FileAccess.FileExists(banks[i])) return (banks[i], tracks[i]);
            }
        }

        return null;
    }
}
