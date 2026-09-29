using SoapAndSoul.Domain.Validation;

namespace SoapAndSoul.Api.Services;

/// <summary>Result of saving a document; REST endpoints and MCP tools translate it to their own format.</summary>
public abstract record SaveOutcome<T>
{
    public sealed record Saved(T Value, bool Created) : SaveOutcome<T>;

    /// <summary>The stored version differs from the one sent; carries the current copy.</summary>
    public sealed record Conflict(T Current) : SaveOutcome<T>;

    public sealed record NotFound : SaveOutcome<T>;

    public sealed record Invalid(ValidationErrors Errors) : SaveOutcome<T>
    {
        public static Invalid Of(string field, string message)
        {
            var errors = new ValidationErrors();
            errors.Add(field, message);
            return new Invalid(errors);
        }
    }
}
