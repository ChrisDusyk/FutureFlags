using FeatureFlags.Domain.Shared;

namespace FeatureFlags.Server.Api;

/// <summary>
/// Turns a domain <see cref="Error"/> into an HTTP response at the API boundary.
/// Lives outside Features/ deliberately: it is boundary plumbing every slice resolves through,
/// not behaviour owned by any one slice.
/// </summary>
public static class ErrorResults
{
    public static IResult ToProblem(this Error error) => Results.Problem(
        detail: error.Message,
        statusCode: StatusCodeFor(error.Type),
        title: TitleFor(error.Type),
        extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    private static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorType type) => type switch
    {
        ErrorType.Validation => "Validation failed",
        ErrorType.Unauthorized => "Unauthorized",
        ErrorType.Forbidden => "Forbidden",
        ErrorType.NotFound => "Not found",
        ErrorType.Conflict => "Conflict",
        _ => "An unexpected error occurred"
    };
}
