using System.Text.Json;
using System.Text.Json.Nodes;

namespace SoapAndSoul.Api.Llm;

/// <summary>
/// Provider-neutral access to a language model. Features describe the answer they need as a JSON
/// schema; each provider maps that to its own structured-output mechanism. Pick the provider with
/// <c>Llm:Provider</c>; add one by implementing this interface and registering it in <see cref="LlmSetup"/>.
/// </summary>
public interface ILlmClient
{
    /// <summary>False when no provider is configured; features should report themselves as unavailable.</summary>
    bool IsConfigured { get; }

    /// <returns>A JSON document matching <see cref="LlmJsonRequest.Schema"/>.</returns>
    /// <exception cref="LlmException">The model is unavailable, declined, or returned no usable answer.</exception>
    Task<string> CompleteJsonAsync(LlmJsonRequest request, CancellationToken ct);
}

/// <param name="System">Instructions for the task.</param>
/// <param name="Prompt">The input for this request.</param>
/// <param name="Schema">JSON schema of the answer (objects must list every property as required, no extra properties).</param>
public sealed record LlmJsonRequest(string System, string Prompt, JsonObject Schema, int MaxTokens = 16000);

/// <summary>A failure the user can be told about; <see cref="Exception.Message"/> is shown in the UI (Ukrainian).</summary>
public sealed class LlmException(string message, Exception? inner = null) : Exception(message, inner);

public static class LlmClientExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<T> CompleteJsonAsync<T>(this ILlmClient llm, LlmJsonRequest request, CancellationToken ct)
    {
        var text = await llm.CompleteJsonAsync(request, ct);
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json) ?? throw new LlmException("Модель повернула порожню відповідь.");
        }
        catch (JsonException e)
        {
            throw new LlmException("Модель повернула відповідь у неочікуваному форматі.", e);
        }
    }
}

/// <summary>Used when <c>Llm:Provider</c> is <c>None</c>.</summary>
internal sealed class DisabledLlmClient : ILlmClient
{
    public bool IsConfigured => false;

    public Task<string> CompleteJsonAsync(LlmJsonRequest request, CancellationToken ct) =>
        throw new LlmException("Мовну модель не налаштовано.");
}
