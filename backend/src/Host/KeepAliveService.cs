using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeepAlive;

/// <summary>
/// 自己定时请求 /api/health，让 Render 免费层不会因为 15 分钟没有流量而休眠。
/// 进程睡着之后它没法唤醒自己，冷启动仍然靠外部访问；醒着时由它维持。
/// </summary>
internal sealed class KeepAliveService(
    IServer server,
    IConfiguration configuration,
    ILogger<KeepAliveService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Clamp(
            configuration.GetValue("KeepAlive:IntervalMinutes", 10),
            1,
            120));

        try
        {
            // 给 Kestrel 一点时间完成绑定，再取监听地址。
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var target = ResolveTarget();
        if (target is null)
        {
            logger.LogWarning("keep-alive 找不到本机监听地址，自维持未启动");
            return;
        }

        logger.LogInformation(
            "keep-alive 已启动：每 {Minutes} 分钟请求 {Target}",
            interval.TotalMinutes,
            target);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var response = await client.GetAsync(target, stoppingToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("keep-alive 请求返回 {Status}", (int)response.StatusCode);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    logger.LogWarning(error, "keep-alive 请求失败");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停机。
        }
    }

    private string? ResolveTarget()
    {
        var configured = configuration["KeepAlive:Url"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        var address = addresses?
            .FirstOrDefault(item => item.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses?.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(address))
        {
            // 兜底：容器里注入的 ASPNETCORE_URLS / PORT。
            address = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            var port = Environment.GetEnvironmentVariable("PORT");
            return int.TryParse(port, out var parsed) ? $"http://127.0.0.1:{parsed}/api/health" : null;
        }

        var loopback = address
            .Replace("[::]", "127.0.0.1", StringComparison.Ordinal)
            .Replace("+", "127.0.0.1", StringComparison.Ordinal)
            .Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)
            .Replace("*", "127.0.0.1", StringComparison.Ordinal);

        return Uri.TryCreate(loopback, UriKind.Absolute, out var uri)
            ? $"http://127.0.0.1:{uri.Port}/api/health"
            : null;
    }
}
