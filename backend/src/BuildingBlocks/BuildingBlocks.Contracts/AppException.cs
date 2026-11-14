namespace BuildingBlocks.Contracts;

/// <summary>可预期的业务失败，由 Host 统一转换成 ProblemDetails。</summary>
public class AppException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}

public sealed class NotFoundException(string message) : AppException("not_found", message, 404);

public sealed class ForbiddenException(string message) : AppException("forbidden", message, 403);

public sealed class ConflictException(string message) : AppException("conflict", message, 409);

/// <summary>字段级校验失败，Errors 会原样返回给前端用于表单提示。</summary>
public sealed class ValidationFailedException : AppException
{
    public ValidationFailedException(IDictionary<string, string[]> errors)
        : base("validation_failed", "请求内容不合法", 422)
    {
        Errors = errors;
    }

    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }

    public IDictionary<string, string[]> Errors { get; }
}
