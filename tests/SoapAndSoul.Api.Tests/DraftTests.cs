using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SoapAndSoul.Api.Llm;
using SoapAndSoul.Domain.Catalog;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Tests;

public class DraftTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Answers with a fixed JSON document and remembers the request.</summary>
    private sealed class FakeLlm(Func<LlmJsonRequest, string> answer) : ILlmClient
    {
        public LlmJsonRequest? Last { get; private set; }
        public bool IsConfigured => true;

        public Task<string> CompleteJsonAsync(LlmJsonRequest request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(answer(request));
        }
    }

    private HttpClient ClientWith(ILlmClient llm) =>
        factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(llm))).CreateClient();

    private static async Task<IngredientDto> SaveOil(HttpClient http, string name)
    {
        var oil = new IngredientDto(Guid.CreateVersion7(), CosmeticLine.Soap, CategoryKey.EssentialOil, name,
            MeasureUnit.Drop, 5, 10, 200, null, 1, null);
        (await http.PutAsJsonAsync($"/api/ingredients/{oil.Id}", oil)).EnsureSuccessStatusCode();
        return oil;
    }

    [Fact]
    public async Task Without_a_provider_voice_drafts_are_off()
    {
        var http = factory.CreateClient();

        Assert.False((await http.GetFromJsonAsync<FeaturesDto>("/api/features"))!.VoiceDrafts);
        var res = await http.PostAsJsonAsync("/api/recipe-drafts", new RecipeDraftRequest(CosmeticLine.Soap, "лаванда"));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Draft_keeps_catalog_matches_and_drops_invented_ids()
    {
        var http = factory.CreateClient();
        var lavender = await SaveOil(http, "Лаванда " + Guid.NewGuid().ToString("N")[..6]);
        var llm = new FakeLlm(_ => new JsonObject
        {
            ["name"] = "  Лавандове  ",
            ["description"] = null,
            ["weight"] = -5,
            ["timeMinutes"] = 40,
            ["batchSize"] = 0,
            ["items"] = new JsonArray(
                new JsonObject { ["spokenName"] = "лаванда", ["ingredientId"] = lavender.Id.ToString(), ["category"] = "EssentialOil", ["amount"] = 15, ["unit"] = "Drop" },
                new JsonObject { ["spokenName"] = "мед", ["ingredientId"] = Guid.NewGuid().ToString(), ["category"] = "Bottle", ["amount"] = null, ["unit"] = "Cup" }),
            ["notes"] = new JsonArray("  ", "Не зрозуміла слово «блискітки»"),
        }.ToJsonString());
        http = ClientWith(llm);

        var res = await http.PostAsJsonAsync("/api/recipe-drafts", new RecipeDraftRequest(CosmeticLine.Soap, "лавандове мило, п'ятнадцять крапель лаванди, мед"));
        res.EnsureSuccessStatusCode();
        var draft = (await res.Content.ReadFromJsonAsync<RecipeDraftDto>())!;

        Assert.Equal("Лавандове", draft.Name);
        Assert.Null(draft.Weight);
        Assert.Equal(40, draft.TimeMinutes);
        Assert.Null(draft.BatchSize);
        Assert.Equal(new RecipeDraftItemDto("лаванда", lavender.Id, CategoryKey.EssentialOil, 15, MeasureUnit.Drop), draft.Items[0]);
        Assert.Equal(new RecipeDraftItemDto("мед", null, null, null, null), draft.Items[1]);
        Assert.Equal(["Не зрозуміла слово «блискітки»"], draft.Notes);

        Assert.Contains(lavender.Id.ToString(), llm.Last!.Prompt);
        Assert.Contains("п'ятнадцять крапель", llm.Last.Prompt);
    }

    [Fact]
    public async Task Model_failure_is_reported_as_bad_gateway_with_the_reason()
    {
        var http = ClientWith(new FakeLlm(_ => throw new LlmException("Мовна модель не відповіла.")));

        var res = await http.PostAsJsonAsync("/api/recipe-drafts", new RecipeDraftRequest(CosmeticLine.Soap, "мило"));

        Assert.Equal(HttpStatusCode.BadGateway, res.StatusCode);
        Assert.Contains("Мовна модель не відповіла.", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Anthropic_client_sends_structured_output_request_and_reads_text()
    {
        var handler = new RecordingHandler(
            """
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5",
             "content":[{"type":"text","text":"{\"ok\":true}"}],
             "stop_reason":"end_turn","stop_sequence":null,
             "usage":{"input_tokens":10,"output_tokens":5}}
            """);
        var client = new AnthropicLlmClient(new LlmOptions { ApiKey = "test-key" }, NullLogger<AnthropicLlmClient>.Instance,
            new HttpClient(handler));
        var schema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject(), ["required"] = new JsonArray(), ["additionalProperties"] = false };

        var text = await client.CompleteJsonAsync(new LlmJsonRequest("system", "prompt", schema), CancellationToken.None);

        Assert.Equal("""{"ok":true}""", text);
        var body = JsonNode.Parse(handler.Body!)!;
        Assert.Equal("claude-opus-5-5", (string?)body["model"]);
        Assert.Equal("default", (string?)body["fallbacks"]);
        Assert.Equal("low", (string?)body["output_config"]!["effort"]);
        Assert.Equal("json_schema", (string?)body["output_config"]!["format"]!["type"]);
        Assert.Null(body["thinking"]);
        Assert.Contains("server-side-fallback-2026-07-01", handler.Betas);
    }

    [Fact]
    public async Task Anthropic_refusal_becomes_a_readable_error()
    {
        var handler = new RecordingHandler(
            """
            {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5-5","content":[],
             "stop_reason":"refusal","stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":0}}
            """);
        var client = new AnthropicLlmClient(new LlmOptions { ApiKey = "test-key" }, NullLogger<AnthropicLlmClient>.Instance,
            new HttpClient(handler));

        await Assert.ThrowsAsync<LlmException>(() =>
            client.CompleteJsonAsync(new LlmJsonRequest("s", "p", new JsonObject { ["type"] = "object" }), CancellationToken.None));
    }

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string Betas { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Betas = request.Headers.TryGetValues("anthropic-beta", out var v) ? string.Join(",", v) : "";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
