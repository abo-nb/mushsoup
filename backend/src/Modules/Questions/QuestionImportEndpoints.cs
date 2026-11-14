using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Questions.Contracts;

namespace Questions;

internal static class QuestionImportEndpoints
{
    public static void MapQuestionImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapPost("/banks/{bankId}/imports/preview", async (
            string bankId,
            ImportRequest request,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.PreviewAsync(bankId, request.Payload ?? "", cancellationToken)));

        group.MapPost("/banks/{bankId}/imports", async (
            string bankId,
            ImportRequest request,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ImportAsync(bankId, request, cancellationToken)));

        group.MapPost("/banks/{bankId}/imports/jobs", async (
            string bankId,
            ImportJobStartRequest request,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.StartJobAsync(bankId, request, cancellationToken)));

        group.MapGet("/imports/jobs/{jobId}", (
            string jobId,
            QuestionImportService service) =>
            service.GetJob(jobId) is { } job ? Results.Ok(job) : Results.NotFound());

        group.MapGet("/banks/{bankId}/imports", async (
            string bankId,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ListBatchesAsync(bankId, cancellationToken)));

        group.MapGet("/imports/{batchId}", async (
            string batchId,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.GetBatchAsync(batchId, cancellationToken)));

        group.MapPost("/imports/{batchId}/approve", async (
            string batchId,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.ApproveAsync(batchId, cancellationToken)));

        group.MapGet("/banks/{bankId}/import-prompt", async (
            string bankId,
            QuestionImportService service,
            CancellationToken cancellationToken) =>
            Results.Ok(await service.BuildPromptAsync(bankId, cancellationToken)));
    }
}
