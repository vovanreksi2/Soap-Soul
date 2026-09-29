using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace SoapAndSoul.Api.Llm;

/// <summary>Claude through the official Anthropic SDK, with structured JSON output.</summary>
/// <param name="http">Replaces the SDK's HTTP client (tests).</param>
internal sealed class AnthropicLlmClient(LlmOptions options, ILogger<AnthropicLlmClient> logger, HttpClient? http = null) : ILlmClient
{
    // If the model declines for policy reasons, the API retries on its recommended fallback model.
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AnthropicClient _client = Create(options, http);

    private static AnthropicClient Create(LlmOptions options, HttpClient? http)
    {
        if (http is not null) return new AnthropicClient { ApiKey = options.ApiKey, Timeout = options.Timeout, HttpClient = http };
        // Without a configured key the SDK finds credentials itself (ANTHROPIC_API_KEY, `ant auth login` profile, ...).
        return string.IsNullOrWhiteSpace(options.ApiKey)
            ? new AnthropicClient { Timeout = options.Timeout }
            : new AnthropicClient { ApiKey = options.ApiKey, Timeout = options.Timeout };
    }

    public bool IsConfigured => true;

    public async Task<string> CompleteJsonAsync(LlmJsonRequest request, CancellationToken ct)
    {
        BetaMessage response;
        try
        {
            response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = options.Model,
                MaxTokens = request.MaxTokens,
                Betas = [FallbackBeta],
                Fallbacks = new Default(),
                System = request.System,
                OutputConfig = new BetaOutputConfig
                {
                    Effort = ToEffort(options.Effort),
                    Format = new BetaJsonOutputFormat { Schema = ToSchema(request.Schema) },
                },
                Messages = [new() { Role = Role.User, Content = request.Prompt }],
            }, ct);
        }
        catch (AnthropicRateLimitException e)
        {
            throw new LlmException("Мовна модель зараз перевантажена. Спробуйте за хвилину.", e);
        }
        catch (AnthropicApiException e)
        {
            logger.LogError(e, "Anthropic API request failed");
            throw new LlmException("Мовна модель не відповіла. Спробуйте ще раз.", e);
        }
        catch (HttpRequestException e)
        {
            logger.LogError(e, "Anthropic API is unreachable");
            throw new LlmException("Немає з’єднання з мовною моделлю.", e);
        }

        if (response.StopReason == "refusal")
            throw new LlmException("Модель відмовилася обробити цей текст.");
        if (response.StopReason == "max_tokens")
            throw new LlmException("Текст задовгий — продиктуйте коротше.");

        var text = new StringBuilder();
        foreach (var block in response.Content)
            if (block.TryPickText(out var t)) text.Append(t.Text);
        if (text.Length == 0) throw new LlmException("Модель повернула порожню відповідь.");
        return text.ToString();
    }

    private static Effort ToEffort(LlmEffort effort) => effort switch
    {
        LlmEffort.Low => Effort.Low,
        LlmEffort.Medium => Effort.Medium,
        LlmEffort.High => Effort.High,
        LlmEffort.XHigh => Effort.Xhigh,
        LlmEffort.Max => Effort.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
    };

    private static Dictionary<string, JsonElement> ToSchema(System.Text.Json.Nodes.JsonObject schema) =>
        JsonSerializer.SerializeToElement(schema).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
}
