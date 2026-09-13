using BaseLib.Config;

namespace NotEnoughDifficulty.NotEnoughDifficultyCode;

/// <summary>
///     Partial: 行为开关字段。
///
/// 第二轮改造删除的旧开关：
/// - <c>ExcludedEncounterIdsCsv</c> / <c>RemovalOnlyActs45</c>：敌人移除列表整个功能已删（需求4）
/// - <c>Act5_ShowDisguisedBossWarning</c> / <c>Act5_AvoidFinalBossEqualPenultimate</c>：
///   属于旧的"第5层中段伪装 boss"设计，该设计已被"直线 BOSS 路线"取代（需求3）
/// </summary>
internal partial class NotEnoughDifficultyConfig
{
    /// <summary>
    ///     跑图各层（1~5）是否启用「额外强化」（见 .Difficulty.cs 的公式）。
    ///     注意：这个开关是**按层**的，放在这里只是为了让 1~5 层的开关定义集中可见；
    ///     实际字段声明在 .Difficulty.cs（与 X/Y 系数同 section，便于配置页阅读）。
    /// </summary>
    // （无字段——保留本 partial 以维持文件结构；实际开关见 .Difficulty.cs）

    /// <summary>
    ///     是否让 act4 玩家走的相邻战斗节点 encounter 不重复（避免连续打同样的怪组合）。
    ///     实现：CustomActEncounterReplacementPatch fill 完 list 之后用 EncounterDeduplicator
    ///     贪心重排（"任务调度: 重排相邻字符"算法）。
    ///     不可避免的情况：池子小（用户把 act1/2 权重设为 0 只留 act3 等）时某个 encounter 频率
    ///     超过 (N+1)/2，数学上无法完全 dedup——log warn 提示但不影响游戏。
    /// </summary>
    public static bool AvoidAdjacentEncounterDuplicate { get; set; } = true;
}
