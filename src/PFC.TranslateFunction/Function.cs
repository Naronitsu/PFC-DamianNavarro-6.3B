using System.Net.Mime;
using System.Text.Json;
using Google.Cloud.Functions.Framework;
using Google.Cloud.Translation.V2;
using Microsoft.AspNetCore.Http;

namespace PFC.TranslateFunction;

public sealed class Function : IHttpFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task HandleAsync(HttpContext context)
    {
        ApplyCors(context);

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!HttpMethods.IsPost(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            return;
        }

        var mediaType = context.Request.ContentType;
        if (mediaType is not null
            && !mediaType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        TranslateRequest? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<TranslateRequest>(
                context.Request.Body,
                JsonOptions,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync("{\"error\":\"invalid json\"}", context.RequestAborted).ConfigureAwait(false);
            return;
        }

        if (body is null
            || string.IsNullOrWhiteSpace(body.Text)
            || string.IsNullOrWhiteSpace(body.TargetLanguage))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = MediaTypeNames.Application.Json;
            await context.Response.WriteAsync(
                "{\"error\":\"text and targetLanguage are required\"}",
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        var client = TranslationClient.Create();
        var results = await client.TranslateTextAsync(
                new[] { body.Text },
                body.TargetLanguage.Trim(),
                null,
                cancellationToken: context.RequestAborted)
            .ConfigureAwait(false);

        var translated = results.FirstOrDefault()?.TranslatedText ?? "";

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = MediaTypeNames.Application.Json;
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(new { translatedText = translated }),
            context.RequestAborted).ConfigureAwait(false);
    }

    private static void ApplyCors(HttpContext context)
    {
        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        context.Response.Headers["Access-Control-Allow-Methods"] = "POST, OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
    }
}

internal sealed class TranslateRequest
{
    public string Text { get; set; } = "";
    public string TargetLanguage { get; set; } = "";
}
