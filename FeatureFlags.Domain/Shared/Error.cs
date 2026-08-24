namespace FeatureFlags.Domain.Shared;

public enum ErrorType
{
    Failure,
    Validation,
    NotFound,
    Conflict,
    Unauthorized,

    /// <summary>
    /// The caller is who they say they are and still may not do this. Distinct from
    /// <see cref="Unauthorized"/>, which means "prove who you are" — answering 401 to a credential
    /// that is perfectly valid but of the wrong kind sends somebody hunting for a revoked key.
    /// </summary>
    Forbidden
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
