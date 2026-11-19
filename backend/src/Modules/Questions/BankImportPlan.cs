using Banks.Contracts;
using Questions.Contracts;

namespace Questions;

internal sealed record ImportBankPlan(UpdateBankRequest Request);

internal sealed record ImportPoolEntryPlan(string Text, List<string> Tags);

/// <summary>
/// 选项池的改动计划。新建池的 Id 在预览阶段就定下来，
/// 这样本批题目引用新池时可以直接写进 DistractorRule，导入时按同一个 Id 落库。
/// </summary>
internal sealed record ImportPoolPlan(
    string Name,
    string? Description,
    string PoolId,
    bool IsNew,
    bool Delete,
    bool Replace,
    List<ImportPoolEntryPlan> NewEntries,
    List<string> EnableTexts,
    List<string> DisableTexts,
    int TotalAfter)
{
    public ImportPoolChangeDto ToDto() => new(
        Name,
        Delete ? "delete" : Replace ? "replace" : "merge",
        Delete,
        NewEntries.Count,
        EnableTexts.Count,
        DisableTexts.Count,
        TotalAfter);
}
