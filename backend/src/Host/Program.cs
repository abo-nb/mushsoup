using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Assets;
using Banks;
using BuildingBlocks.Contracts;
using BuildingBlocks.Infrastructure;
using Classes;
using Identity;
using Leaderboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Notes;
using Practice;
using Questions;
using Recents;
using Scheduling;

var builder = WebApplication.CreateBuilder(args);

// 本地密钥放在 appsettings.Local.json（已 gitignore）；环境变量仍然优先，方便部署覆盖。
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

// 导入题目允许单次 50MB 内容，请求体上限要留出 JSON 转义与中文 UTF-8 的余量。
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 128L * 1024 * 1024);

var mongoOptions = builder.Configuration.GetSection("Mongo").Get<MongoOptions>() ?? new MongoOptions();
IDisposable? embeddedMongo = null;
if (mongoOptions.UseEmbedded)
{
    embeddedMongo = EmbeddedMongo.Start(mongoOptions);
    builder.Services.AddSingleton(embeddedMongo);
}

builder.Services.AddSingleton(mongoOptions);
builder.Services.AddSingleton<MongoUsageGuard>();
builder.Services.AddSingleton<MongoDatabaseProvider>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ms_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddOpenApi();

IModule[] modules =
[
    new IdentityModule(),
    new ClassesModule(),
    new BanksModule(),
    new QuestionsModule(),
    new PracticeModule(),
    new SchedulingModule(),
    new AssetsModule(),
    new NotesModule(),
    new LeaderboardModule(),
    new RecentsModule(),
];

foreach (var module in modules)
{
    module.Register(builder.Services, builder.Configuration);
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok", time = DateTime.UtcNow }));
app.MapOpenApi();

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

var webRoot = app.Environment.WebRootPath;
if (!string.IsNullOrEmpty(webRoot) && Directory.Exists(webRoot))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallbackToFile("index.html");
}

using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    foreach (var module in modules)
    {
        try
        {
            await module.EnsureIndexesAsync(scope.ServiceProvider, CancellationToken.None);
            logger.LogInformation("Module {Module} indexes ready", module.Name);
        }
        catch (Exception ex)
        {
            if (mongoOptions.IgnoreIndexErrors)
            {
                logger.LogWarning("Module {Module} index setup skipped: {Message}", module.Name, ex.Message);
                continue;
            }

            logger.LogError(ex, "Module {Module} index setup failed", module.Name);
            throw;
        }
    }
}

app.Run();

internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? DisplayName => Principal?.FindFirstValue(ClaimTypes.Name);

    public string? ClassId => Principal?.FindFirstValue("class_id");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string RequireUserId() =>
        UserId ?? throw new AppException("unauthorized", "未登录", StatusCodes.Status401Unauthorized);
}

internal sealed class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var status = StatusCodes.Status500InternalServerError;
        var code = "internal_error";
        var detail = "服务器内部错误";
        IDictionary<string, string[]>? errors = null;

        switch (exception)
        {
            case ValidationFailedException validation:
                status = validation.StatusCode;
                code = validation.Code;
                detail = validation.Message;
                errors = validation.Errors;
                break;
            case AppException appException:
                status = appException.StatusCode;
                code = appException.Code;
                detail = appException.Message;
                break;
            default:
                logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                break;
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["code"] = code;
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

public partial class Program;
