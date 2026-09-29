using Microsoft.Extensions.Options;

namespace SoapAndSoul.Api.Llm;

public enum LlmProvider { None, Anthropic }

public enum LlmEffort { Low, Medium, High, XHigh, Max }

public sealed class LlmOptions
{
    public LlmProvider Provider { get; set; } = LlmProvider.None;

    public string Model { get; set; } = "claude-opus-5-5";

    /// <summary>Falls back to the SDK's own lookup (<c>ANTHROPIC_API_KEY</c> etc.) when empty.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Parsing a dictated recipe is short extraction work, so low effort keeps it fast.</summary>
    public LlmEffort Effort { get; set; } = LlmEffort.Low;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);
}

public static class LlmSetup
{
    public static void AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));
        services.AddSingleton<ILlmClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            var logger = sp.GetRequiredService<ILogger<AnthropicLlmClient>>();
            return options.Provider switch
            {
                LlmProvider.None => new DisabledLlmClient(),
                // Outside development a missing key would fail every request; keep the feature off instead.
                LlmProvider.Anthropic when !HasAnthropicKey(options) && !sp.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    => Disabled(logger, "Voice drafts are off: Llm:ApiKey is not set."),
                LlmProvider.Anthropic => new AnthropicLlmClient(options, logger),
                _ => throw new InvalidOperationException("Llm:Provider must be None or Anthropic."),
            };
        });
    }

    private static bool HasAnthropicKey(LlmOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"));

    private static DisabledLlmClient Disabled(ILogger logger, string reason)
    {
        logger.LogWarning("{Reason}", reason);
        return new DisabledLlmClient();
    }
}
