using BuildingBlocks.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Classes;

public static class ClassesEndpoints
{
    public static void MapClassesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/classes").RequireAuthorization();

        group.MapGet("/current", async (
            ClassService classes,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var classId = currentUser.ClassId;
            var klass = string.IsNullOrEmpty(classId)
                ? await classes.GetDefaultAsync(cancellationToken)
                : await classes.GetAsync(classId, cancellationToken) ?? throw new NotFoundException("班级不存在");

            return Results.Ok(klass);
        });
    }
}
