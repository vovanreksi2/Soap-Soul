using System.Diagnostics;
using SoapAndSoul.Api.Services;
using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Features;

internal static class ApiResults
{
    public static IResult Invalid(ValidationErrors errors) => Results.ValidationProblem(errors);

    public static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>409 with the current server copy, so the client can reload it.</summary>
    public static IResult Conflict<T>(T current) => Results.Conflict(current);

    public static IResult ToResult<T>(this SaveOutcome<T> outcome, Func<T, string> location) => outcome switch
    {
        SaveOutcome<T>.Saved { Created: true, Value: var v } => Results.Created(location(v), v),
        SaveOutcome<T>.Saved { Value: var v } => Results.Ok(v),
        SaveOutcome<T>.Conflict { Current: var current } => Conflict(current),
        SaveOutcome<T>.NotFound => Results.NotFound(),
        SaveOutcome<T>.Invalid { Errors: var errors } => Invalid(errors),
        _ => throw new UnreachableException(),
    };
}
