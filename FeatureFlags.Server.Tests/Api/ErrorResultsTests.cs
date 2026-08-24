using FeatureFlags.Domain.Shared;
using FeatureFlags.Server.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FeatureFlags.Server.Tests.Api;

public class ErrorResultsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Unauthorized, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Failure, StatusCodes.Status500InternalServerError)]
    public void ToProblem_ShouldMapErrorTypeToStatusCode(ErrorType type, int expectedStatusCode)
    {
        var problem = ToProblem(new Error("Some.Code", "Something went wrong.", type));

        Assert.Equal(expectedStatusCode, problem.StatusCode);
        Assert.Equal(expectedStatusCode, problem.ProblemDetails.Status);
    }

    [Fact]
    public void ToProblem_ShouldCarryMessageAsDetail()
    {
        var problem = ToProblem(Error.Validation("Flag.Key.Required", "A flag key is required."));

        Assert.Equal("A flag key is required.", problem.ProblemDetails.Detail);
    }

    [Fact]
    public void ToProblem_ShouldExposeErrorCodeAsExtension()
    {
        var problem = ToProblem(Error.Conflict("Flag.DuplicateKey", "Already exists."));

        // Clients branch on the stable code, not the human-readable detail.
        Assert.Equal("Flag.DuplicateKey", Assert.Contains("code", problem.ProblemDetails.Extensions));
    }

    [Theory]
    [InlineData(ErrorType.Validation, "Validation failed")]
    [InlineData(ErrorType.Conflict, "Conflict")]
    [InlineData(ErrorType.NotFound, "Not found")]
    public void ToProblem_ShouldTitleByErrorType(ErrorType type, string expectedTitle)
    {
        var problem = ToProblem(new Error("Some.Code", "Message.", type));

        Assert.Equal(expectedTitle, problem.ProblemDetails.Title);
    }

    private static ProblemHttpResult ToProblem(Error error) =>
        Assert.IsType<ProblemHttpResult>(error.ToProblem());
}
