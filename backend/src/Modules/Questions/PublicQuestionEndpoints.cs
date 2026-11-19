using Banks.Contracts;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Questions.Contracts;

namespace Questions;

/// <summary>
/// 未登录也能用的只读入口，仅对「公开题库 + 已发布题目」开放。
/// 返回的卡片不含正确答案，判分在服务端完成。
/// </summary>
internal static class PublicQuestionEndpoints
{
    internal static void MapPublicQuestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public");

        group.MapGet("/banks/{bankId}/questions", async (
            string bankId,
            IBankDirectory banks,
            QuestionCatalog catalog,
            CancellationToken cancellationToken) =>
        {
            await EnsurePublicBankAsync(banks, bankId, cancellationToken);
            return Results.Ok(await catalog.GetPublicSummariesAsync(bankId, cancellationToken));
        });

        group.MapGet("/banks/{bankId}/questions/{questionId}/card", async (
            string bankId,
            string questionId,
            int? attempt,
            IBankDirectory banks,
            IQuestionCatalog catalog,
            ICardFactory cards,
            CancellationToken cancellationToken) =>
        {
            await EnsurePublicBankAsync(banks, bankId, cancellationToken);
            var question = await LoadPublishedAsync(catalog, bankId, questionId, cancellationToken);
            var shuffle = question.Shuffle != ShuffleMode.Never;
            // 游客没有作答记录，用客户端带来的 attempt 换 seed，重练时不会总是同一组选项。
            var attemptNo = Math.Clamp(attempt ?? 0, 0, 100_000);
            return Results.Ok(await cards.BuildAsync(question, "", attemptNo, shuffle, cancellationToken));
        });

        group.MapPost("/banks/{bankId}/questions/{questionId}/answer", async (
            string bankId,
            string questionId,
            GuestAnswerRequest request,
            IBankDirectory banks,
            IQuestionCatalog catalog,
            IQuestionGrader grader,
            CancellationToken cancellationToken) =>
        {
            await EnsurePublicBankAsync(banks, bankId, cancellationToken);
            var question = await LoadPublishedAsync(catalog, bankId, questionId, cancellationToken);
            var selected = request.SelectedKeys ?? [];
            var grade = grader.Grade(question, selected);
            var correctKeys = question.Options
                .Where(option => option.Correct)
                .Select(option => option.Key)
                .ToList();
            return Results.Ok(new GuestAnswerResultDto(
                grade.Correct,
                correctKeys,
                question.Stem,
                question.Explanation));
        });
    }

    private static async Task EnsurePublicBankAsync(
        IBankDirectory banks,
        string bankId,
        CancellationToken cancellationToken)
    {
        var access = await banks.GetAccessAsync(bankId, "", null, cancellationToken);
        if (access is null || !access.CanView || access.Bank.Visibility != BankVisibility.Public)
        {
            throw new NotFoundException("题库不存在");
        }
    }

    private static async Task<QuestionDto> LoadPublishedAsync(
        IQuestionCatalog catalog,
        string bankId,
        string questionId,
        CancellationToken cancellationToken)
    {
        var question = await catalog.GetAsync(questionId, cancellationToken);
        if (question is null
            || question.BankId != bankId
            || question.Status != QuestionStatus.Published)
        {
            throw new NotFoundException("题目不存在");
        }

        return question;
    }
}
