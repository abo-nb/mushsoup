using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Practice.Contracts;

namespace Practice;

static class PracticeEndpoints
{
    public static void MapPracticeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapPost("/banks/{bankId}/sessions", async (
            string bankId,
            StartSessionRequest request,
            PracticeService practice,
            CancellationToken cancellationToken) =>
            Results.Ok(await practice.StartSessionAsync(bankId, request, cancellationToken)));

        group.MapGet("/sessions/{id}", async (
            string id,
            PracticeService practice,
            CancellationToken cancellationToken) =>
            Results.Ok(await practice.GetSessionAsync(id, cancellationToken)));

        group.MapGet("/sessions/{id}/next", async (
            string id,
            PracticeService practice,
            CancellationToken cancellationToken) =>
            Results.Ok(await practice.GetNextAsync(id, cancellationToken)));

        group.MapPost("/sessions/{id}/answers", async (
            string id,
            SubmitAnswerRequest request,
            PracticeService practice,
            CancellationToken cancellationToken) =>
            Results.Ok(await practice.SubmitAnswerAsync(id, request, cancellationToken)));

        group.MapPost("/sessions/{id}/finish", async (
            string id,
            PracticeService practice,
            CancellationToken cancellationToken) =>
            Results.Ok(await practice.FinishAsync(id, cancellationToken)));
    }
}
