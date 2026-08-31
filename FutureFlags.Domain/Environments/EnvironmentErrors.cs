using FutureFlags.Domain.Shared;

namespace FutureFlags.Domain.Environments;

public static class EnvironmentErrors
{
    public static Error KeyRequired => Error.Validation(
        "Environment.Key.Required",
        "An environment is required.");

    public static Error KeyUnrecognized(string value) => Error.Validation(
        "Environment.Key.Unrecognized",
        $"'{value}' is not an environment this application recognizes.");
}
