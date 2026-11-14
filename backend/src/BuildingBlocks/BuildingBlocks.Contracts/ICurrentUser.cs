namespace BuildingBlocks.Contracts;

public interface ICurrentUser
{
    string? UserId { get; }

    string? DisplayName { get; }

    string? ClassId { get; }

    bool IsAuthenticated { get; }

    string RequireUserId();
}
