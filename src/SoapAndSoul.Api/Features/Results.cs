using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Features;

internal static class ApiResults
{
    public static IResult Invalid(ValidationErrors errors) => Results.ValidationProblem(errors);

    public static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>409 with the current server copy, so the client can reload it.</summary>
    public static IResult Conflict<T>(T current) => Results.Conflict(current);
}
