using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Contracts;

/// <summary>
/// 一个功能板块。Host 只负责按约定注册它们，不关心板块内部结构。
/// </summary>
public interface IModule
{
    string Name { get; }

    void Register(IServiceCollection services, IConfiguration configuration);

    void MapEndpoints(IEndpointRouteBuilder app);

    Task EnsureIndexesAsync(IServiceProvider services, CancellationToken cancellationToken);
}
