// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.Health.Fhir.Mcp;

internal sealed class FhirClient : IFhirClient
{
    private static readonly string[] MetadataPath = ["metadata"];
    private readonly FhirMcpOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IFhirAccessTokenProvider _tokenProvider;
    private readonly IFhirCaptureWriter _captureWriter;
    private readonly object _resourceTypesSync = new();
    private Task<HashSet<string>>? _resourceTypesTask;

    public FhirClient(
        FhirMcpOptions options,
        HttpClient httpClient,
        IFhirAccessTokenProvider tokenProvider,
        IFhirCaptureWriter captureWriter)
    {
        _options = options;
        _httpClient = httpClient;
        _tokenProvider = tokenProvider;
        _captureWriter = captureWriter;
    }

    public async Task EnsureResourceTypeAsync(string resourceType, CancellationToken cancellationToken)
    {
        Task<HashSet<string>> resourceTypesTask;
        lock (_resourceTypesSync)
        {
            _resourceTypesTask ??= LoadResourceTypesAsync();
            resourceTypesTask = _resourceTypesTask;
        }

        HashSet<string> resourceTypes;
        try
        {
            resourceTypes = await resourceTypesTask.WaitAsync(cancellationToken);
        }
        catch
        {
            lock (_resourceTypesSync)
            {
                if (_resourceTypesTask == resourceTypesTask)
                {
                    _resourceTypesTask = null;
                }
            }

            throw;
        }

        if (!resourceTypes.Contains(resourceType))
        {
            throw new ArgumentException($"Resource type '{resourceType}' is not advertised by the FHIR server.", nameof(resourceType));
        }
    }

    public Task<FhirResponse> GetAsync(
        string operationName,
        IReadOnlyList<string> pathSegments,
        IReadOnlyList<KeyValuePair<string, string>> queryParameters,
        CancellationToken cancellationToken) =>
        SendAsync(operationName, HttpMethod.Get, pathSegments, queryParameters, requestBody: null, cancellationToken);

    public Task<FhirResponse> PostAsync(
        string operationName,
        IReadOnlyList<string> pathSegments,
        string requestBody,
        CancellationToken cancellationToken) =>
        SendAsync(operationName, HttpMethod.Post, pathSegments, Array.Empty<KeyValuePair<string, string>>(), requestBody, cancellationToken);

    private async Task<FhirResponse> SendAsync(
        string operationName,
        HttpMethod method,
        IReadOnlyList<string> pathSegments,
        IReadOnlyList<KeyValuePair<string, string>> queryParameters,
        string? requestBody,
        CancellationToken cancellationToken)
    {
        Uri requestUri = BuildUri(_options.BaseAddress, pathSegments, queryParameters);
        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

        string? token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (requestBody is not null)
        {
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/fhir+json");
        }

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage? response = null;
        string? responseBody = null;
        string? captureDirectory = null;

        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            responseBody = await ReadBoundedContentAsync(response.Content, _options.MaxResponseBytes, cancellationToken);
            stopwatch.Stop();
            captureDirectory = await _captureWriter.CaptureAsync(
                operationName,
                method,
                requestUri,
                requestBody,
                response,
                responseBody,
                stopwatch.Elapsed,
                error: null,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new FhirRequestException(
                    $"FHIR request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).{GetOperationOutcomeDiagnostic(responseBody)}",
                    captureDirectory);
            }

            JsonElement resource = ParseFhirResource(responseBody);
            return new FhirResponse((int)response.StatusCode, requestUri, resource, captureDirectory);
        }
        catch (FhirRequestException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            captureDirectory ??= await _captureWriter.CaptureAsync(
                operationName,
                method,
                requestUri,
                requestBody,
                response,
                responseBody,
                stopwatch.Elapsed,
                exception.Message,
                cancellationToken);
            throw new FhirRequestException($"FHIR request failed: {exception.Message}", captureDirectory, exception);
        }
        finally
        {
            response?.Dispose();
        }
    }

    private async Task<HashSet<string>> LoadResourceTypesAsync()
    {
        FhirResponse response = await GetAsync(
            "discover-fhir-resource-types",
            MetadataPath,
            Array.Empty<KeyValuePair<string, string>>(),
            CancellationToken.None);
        var resourceTypes = new HashSet<string>(StringComparer.Ordinal);

        if (response.Resource.TryGetProperty("rest", out JsonElement restElements))
        {
            foreach (JsonElement rest in restElements.EnumerateArray())
            {
                if (!rest.TryGetProperty("resource", out JsonElement resources))
                {
                    continue;
                }

                foreach (JsonElement resource in resources.EnumerateArray())
                {
                    if (resource.TryGetProperty("type", out JsonElement typeElement) && typeElement.GetString() is string resourceType)
                    {
                        resourceTypes.Add(resourceType);
                    }
                }
            }
        }

        if (resourceTypes.Count == 0)
        {
            throw new InvalidOperationException("FHIR CapabilityStatement did not advertise any resource types.");
        }

        return resourceTypes;
    }

    private static Uri BuildUri(
        Uri baseAddress,
        IReadOnlyList<string> pathSegments,
        IReadOnlyList<KeyValuePair<string, string>> queryParameters)
    {
        string relativePath = string.Join('/', pathSegments.Select(Uri.EscapeDataString));
        string query = string.Join(
            '&',
            queryParameters.Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}"));
        return new Uri(baseAddress, query.Length == 0 ? relativePath : $"{relativePath}?{query}");
    }

    private static async Task<string> ReadBoundedContentAsync(HttpContent content, int maxResponseBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maxResponseBytes)
        {
            throw new InvalidOperationException($"FHIR response exceeded the configured {maxResponseBytes} byte limit.");
        }

        await using Stream source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        byte[] buffer = new byte[81920];

        while (true)
        {
            int bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (destination.Length + bytesRead > maxResponseBytes)
            {
                throw new InvalidOperationException($"FHIR response exceeded the configured {maxResponseBytes} byte limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private static JsonElement ParseFhirResource(string responseBody)
    {
        using JsonDocument document = JsonDocument.Parse(responseBody);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("resourceType", out JsonElement resourceType) ||
            resourceType.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("FHIR response was not a JSON resource with a resourceType property.");
        }

        return document.RootElement.Clone();
    }

    private static string GetOperationOutcomeDiagnostic(string responseBody)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("resourceType", out JsonElement resourceType) ||
                resourceType.GetString() != "OperationOutcome" ||
                !document.RootElement.TryGetProperty("issue", out JsonElement issues))
            {
                return string.Empty;
            }

            string? diagnostic = issues.EnumerateArray()
                .Select(issue => issue.TryGetProperty("diagnostics", out JsonElement value) ? value.GetString() : null)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return diagnostic is null ? string.Empty : $" {diagnostic}";
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
