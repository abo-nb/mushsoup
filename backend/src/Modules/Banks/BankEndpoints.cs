using Banks.Contracts;
using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Banks;

internal static class BankEndpoints
{
    public static void MapBankEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/banks").RequireAuthorization();

        group.MapGet("", async (ICurrentUser currentUser, BankService banks, CancellationToken cancellationToken) =>
        {
            var visible = await banks.ListVisibleAsync(currentUser.RequireUserId(), currentUser.ClassId, cancellationToken);
            return Results.Ok(visible);
        });

        group.MapPost("", async (
            CreateBankRequest request,
            ICurrentUser currentUser,
            BankService banks,
            CancellationToken cancellationToken) =>
        {
            var bank = await banks.CreateAsync(request, currentUser.RequireUserId(), currentUser.ClassId, cancellationToken);
            return Results.Created($"/api/banks/{bank.Id}", bank);
        });

        group.MapGet("/{id}", async (
            string id,
            ICurrentUser currentUser,
            BankService banks,
            CancellationToken cancellationToken) =>
        {
            var access = await banks.GetAccessAsync(id, currentUser.RequireUserId(), currentUser.ClassId, cancellationToken);

            if (access is null || !access.CanView)
            {
                throw new NotFoundException("题库不存在");
            }

            return Results.Ok(access);
        });

        group.MapPatch("/{id}", async (
            string id,
            UpdateBankRequest request,
            ICurrentUser currentUser,
            BankService banks,
            CancellationToken cancellationToken) =>
        {
            var bank = await banks.UpdateAsync(id, request, currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(bank);
        });

        group.MapDelete("/{id}", async (
            string id,
            ICurrentUser currentUser,
            BankService banks,
            CancellationToken cancellationToken) =>
        {
            await banks.DeleteAsync(id, currentUser.RequireUserId(), cancellationToken);
            return Results.NoContent();
        });

        group.MapPost("/{id}/members", async (
            string id,
            AddBankMemberRequest request,
            ICurrentUser currentUser,
            BankService banks,
            CancellationToken cancellationToken) =>
        {
            var bank = await banks.AddMemberAsync(id, request, currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(bank);
        });

        group.MapDelete("/{id}/members/{userId}", async (
            string id,
            string userId,
            ICurrentUser currentUser,
            BankService banks,
            CancellationToken cancellationToken) =>
        {
            var bank = await banks.RemoveMemberAsync(id, userId, currentUser.RequireUserId(), cancellationToken);
            return Results.Ok(bank);
        });
    }
}
