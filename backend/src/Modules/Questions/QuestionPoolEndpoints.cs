using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Questions.Contracts;

namespace Questions;

internal static class QuestionPoolEndpoints
{
    public static void MapQuestionPoolEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/banks/{bankId}/pools", async (
            string bankId,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(
                bankId,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken)));

        group.MapPost("/banks/{bankId}/pools", async (
            string bankId,
            CreatePoolRequest request,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
        {
            var pool = await service.CreateAsync(
                bankId,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request,
                cancellationToken);
            return Results.Created($"/api/pools/{pool.Id}", pool);
        });

        group.MapGet("/pools/{id}", async (
            string id,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken)));

        group.MapPatch("/pools/{id}", async (
            string id,
            UpdatePoolRequest request,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request,
                cancellationToken)));

        group.MapDelete("/pools/{id}", async (
            string id,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteAsync(id, currentUser.RequireUserId(), currentUser.ClassId, cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/pools/{id}/entries", async (
            string id,
            CreatePoolEntryRequest request,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.AddEntryAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request,
                cancellationToken)));

        group.MapPatch("/pools/{id}/entries/{entryId}", async (
            string id,
            string entryId,
            UpdatePoolEntryRequest request,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateEntryAsync(
                id,
                entryId,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                request,
                cancellationToken)));

        group.MapDelete("/pools/{id}/entries/{entryId}", async (
            string id,
            string entryId,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
        {
            await service.DeleteEntryAsync(
                id,
                entryId,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/pools/{id}/health", async (
            string id,
            ICurrentUser currentUser,
            QuestionPoolService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.HealthAsync(
                id,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken)));
    }
}
