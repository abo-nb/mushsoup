using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Questions.Contracts;

namespace Questions;

internal static class QuestionEndpoints
{
    public static void MapQuestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/banks/{bankId}/questions", async (
            string bankId,
            string? tag,
            string? q,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(
                bankId,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                tag,
                q,
                cancellationToken)));

        group.MapPost("/banks/{bankId}/questions", async (
            string bankId,
            SaveQuestionRequest request,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
        {
            var question = await service.CreateAsync(
                bankId,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request,
                cancellationToken);
            return Results.Created($"/api/questions/{question.Id}", question);
        });

        group.MapGet("/questions/chapter-index", async (
            string[]? bankIds,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetChapterIndexAsync(
                bankIds ?? [],
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken)));

        group.MapGet("/questions/{id}", async (
            string id,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken)));

        group.MapPut("/questions/{id}", async (
            string id,
            SaveQuestionRequest request,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request,
                cancellationToken)));

        group.MapDelete("/questions/{id}", async (
            string id,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/questions/{id}/revisions", async (
            string id,
            ICurrentUser currentUser,
            QuestionService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRevisionsAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken)));
    }
}
