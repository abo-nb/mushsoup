using Identity.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Identity;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/verify/members", async (
            UserService users,
            bool? refresh,
            CancellationToken cancellationToken) =>
            Results.Ok(await users.GetMembersAsync(refresh == true, cancellationToken)));

        group.MapPost("/verify/request", async (
            VerifyRequest request,
            UserService users,
            CancellationToken cancellationToken) =>
            Results.Ok(await users.RequestVerificationAsync(request, cancellationToken)));

        group.MapPost("/verify/confirm", async (
            VerifyConfirmRequest request,
            UserService users,
            CancellationToken cancellationToken) =>
            Results.Ok(await users.ConfirmVerificationAsync(request, cancellationToken)));

        group.MapPost("/logout", async (UserService users) =>
        {
            await users.LogoutAsync();
            return Results.NoContent();
        });
    }

    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization();

        group.MapGet("", async (UserService users, CancellationToken cancellationToken) =>
            Results.Ok(await users.GetMeAsync(cancellationToken)));

        group.MapPatch("/prefs", async (
            UpdatePrefsRequest request,
            UserService users,
            CancellationToken cancellationToken) =>
            Results.Ok(await users.UpdatePrefsAsync(request, cancellationToken)));
    }
}
