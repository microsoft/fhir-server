// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed class FhirCaptureWriter : IFhirCaptureWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private static readonly Regex InvalidNameCharacterRegex = new("[^A-Za-z0-9_-]+", RegexOptions.CultureInvariant);
    private readonly FhirMcpOptions _options;

    public FhirCaptureWriter(FhirMcpOptions options)
    {
        _options = options;
    }

    public async Task<string> CaptureAsync(
        string operationName,
        HttpMethod method,
        Uri requestUri,
        string? requestBody,
        HttpResponseMessage? response,
        string? responseBody,
        TimeSpan elapsed,
        string? error,
        CancellationToken cancellationToken)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture);
        string captureDirectory = Path.Combine(
            _options.CaptureRoot,
            $"{timestamp}-{SanitizeName(operationName)}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(captureDirectory);

        JsonElement? requestResource = ParseOptionalJson(requestBody);
        var requestCapture = new
        {
            method = method.Method,
            uri = requestUri.AbsoluteUri,
            headers = new Dictionary<string, string>
            {
                ["Accept"] = "application/fhir+json",
                ["Content-Type"] = requestBody is null ? string.Empty : "application/fhir+json",
            },
            body = requestResource,
        };

        var resultCapture = new
        {
            statusCode = response is null ? null : (int?)response.StatusCode,
            reasonPhrase = response?.ReasonPhrase,
            contentType = response?.Content.Headers.ContentType?.ToString(),
            elapsedMilliseconds = elapsed.TotalMilliseconds,
            error,
        };

        await WriteJsonAsync(Path.Combine(captureDirectory, "request.json"), requestCapture, cancellationToken);
        await WriteJsonAsync(Path.Combine(captureDirectory, "result.json"), resultCapture, cancellationToken);

        if (responseBody is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(captureDirectory, "response.fhir.json"), responseBody, cancellationToken);
        }

        return captureDirectory;
    }

    private static JsonElement? ParseOptionalJson(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken);
        await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }

    private static string SanitizeName(string value)
    {
        string sanitized = InvalidNameCharacterRegex.Replace(value, "-").Trim('-');
        return string.IsNullOrEmpty(sanitized) ? "fhir-call" : sanitized;
    }
}
