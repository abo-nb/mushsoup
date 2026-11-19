using System.Text.Json;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Notes.Contracts;

namespace Notes;

internal static class NoteEndpoints
{
    internal static void MapNoteEndpoints(this IEndpointRouteBuilder app)
    {
        // 游客入口：只暴露公开讲义，供短链接在未登录时阅读。
        app.MapGet("/api/public/notes/{id}", async (
            string id,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            var note = await notes.GetVisibleAsync(id, "", cancellationToken)
                ?? throw new NotFoundException("讲义不存在");
            return Results.Ok(note);
        });

        var group = app.MapGroup("/api/notes").RequireAuthorization();

        group.MapGet("", async (
            ICurrentUser currentUser,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            var visible = await notes.ListVisibleAsync(currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(visible);
        });

        group.MapGet("/import-prompt", () => Results.Ok(NotePromptBuilder.Build()));

        group.MapGet("/{id}", async (
            string id,
            ICurrentUser currentUser,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            var note = await notes.GetVisibleAsync(id, currentUser.RequireUserId(), cancellationToken)
                ?? throw new NotFoundException("讲义不存在");
            return Results.Ok(note);
        });

        group.MapPost("", async (
            JsonElement content,
            ICurrentUser currentUser,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            var note = await notes.CreateAsync(
                content,
                currentUser.RequireUserId(),
                currentUser.ClassId,
                cancellationToken);
            return Results.Created($"/api/notes/{note.Id}", note);
        });

        group.MapPut("/{id}", async (
            string id,
            JsonElement content,
            ICurrentUser currentUser,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            var note = await notes.UpdateAsync(id, content, currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(note);
        });

        group.MapPatch("/{id}", async (
            string id,
            NoteMetadataRequest request,
            ICurrentUser currentUser,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            var note = await notes.UpdateMetadataAsync(id, request, currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(note);
        });

        group.MapDelete("/{id}", async (
            string id,
            ICurrentUser currentUser,
            NoteService notes,
            CancellationToken cancellationToken) =>
        {
            await notes.DeleteAsync(id, currentUser.RequireUserId(), cancellationToken);
            return Results.NoContent();
        });
    }
}
