using SoapAndSoul.Api.Drafts;
using SoapAndSoul.Api.Llm;
using SoapAndSoul.Domain.Contracts;

namespace SoapAndSoul.Api.Features;

public static class DraftEndpoints
{
    public static void MapDraftEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/features", (ILlmClient llm) => new FeaturesDto(VoiceDrafts: llm.IsConfigured));
        api.MapPost("/recipe-drafts", Build);
    }

    /// <summary>Structures a dictated recipe; the client reviews and applies the draft itself.</summary>
    private static async Task<IResult> Build(RecipeDraftRequest request, ILlmClient llm, RecipeDraftBuilder builder, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Transcript))
            return ApiResults.Invalid(nameof(request.Transcript), "Текст порожній.");
        if (request.Transcript.Length > RecipeDraftRequest.MaxTranscriptLength)
            return ApiResults.Invalid(nameof(request.Transcript), "Текст задовгий — продиктуйте коротше.");
        if (!llm.IsConfigured)
            return Results.Problem("Розбір голосу не налаштовано на сервері.", statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            return Results.Ok(await builder.BuildAsync(request, ct));
        }
        catch (LlmException e)
        {
            return Results.Problem(e.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
