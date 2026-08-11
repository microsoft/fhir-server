// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Microsoft.Health.Fhir.Mcp.UnitTests;

public class FhirMcpTests
{
    [Fact]
    public async Task GivenSemanticArguments_WhenToolIsCalled_ThenFhirParametersBodyIsConstructed()
    {
        var client = new RecordingFhirClient();
        var tools = new FhirTools(client, CreateOptions(Path.GetTempPath(), maxCount: 10));

        await tools.PatientSemanticSearchAsync(
            "patient-1",
            "previous near-fainting episodes",
            new[] { "Observation", "Observation", "DiagnosticReport" },
            count: 3);

        Assert.Equal(new[] { "Patient", "patient-1", "$semantic-search" }, client.PathSegments);
        using JsonDocument body = JsonDocument.Parse(client.RequestBody!);
        JsonElement parameters = body.RootElement.GetProperty("parameter");
        Assert.Equal("previous near-fainting episodes", parameters[0].GetProperty("valueString").GetString());
        Assert.Equal(3, parameters[1].GetProperty("valueInteger").GetInt32());
        Assert.Equal(new[] { "Observation", "DiagnosticReport" }, parameters.EnumerateArray().Skip(2).Select(value => value.GetProperty("valueCode").GetString()));
    }

    [Fact]
    public async Task GivenRepeatedFilters_WhenStandardSearchIsCalled_ThenEveryValueIsPreserved()
    {
        var client = new RecordingFhirClient();
        var tools = new FhirTools(client, CreateOptions(Path.GetTempPath(), maxCount: 10));
        var filters = new Dictionary<string, string[]>
        {
            ["patient"] = new[] { "patient-1" },
            ["date"] = new[] { "ge2025-01-01", "lt2026-01-01" },
        };

        await tools.SearchFhirResourcesAsync("Observation", filters, count: 4);

        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string>("patient", "patient-1"),
                new KeyValuePair<string, string>("date", "ge2025-01-01"),
                new KeyValuePair<string, string>("date", "lt2026-01-01"),
                new KeyValuePair<string, string>("_count", "4"),
            },
            client.QueryParameters);
    }

    [Fact]
    public async Task GivenCountOutsideConfiguredBounds_WhenToolIsCalled_ThenRequestIsRejected()
    {
        var client = new RecordingFhirClient();
        var tools = new FhirTools(client, CreateOptions(Path.GetTempPath(), maxCount: 5));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            tools.SearchFhirResourcesAsync("Observation", count: 6));

        Assert.Null(client.RequestBody);
        Assert.Empty(client.QueryParameters);
    }

    [Fact]
    public async Task GivenUnsafeResourceId_WhenReadIsCalled_ThenPathIsRejected()
    {
        var client = new RecordingFhirClient();
        var tools = new FhirTools(client, CreateOptions(Path.GetTempPath(), maxCount: 10));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            tools.ReadFhirResourceAsync("Observation", "../Patient/1"));

        Assert.Empty(client.PathSegments);
    }

    [Fact]
    public async Task GivenAuthenticatedSearch_WhenClientSendsRequest_ThenUriIsEncodedAndCaptureOmitsToken()
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), $"fhir-mcp-tests-{Guid.NewGuid():N}");
        try
        {
            var handler = new RecordingHttpMessageHandler(CreateFhirResponse("Bundle"));
            using var httpClient = new HttpClient(handler);
            var options = CreateOptions(captureRoot, maxCount: 10);
            var captureWriter = new FhirCaptureWriter(options);
            var client = new FhirClient(options, httpClient, new FixedTokenProvider("secret-token"), captureWriter);

            FhirResponse result = await client.GetAsync(
                "encoded-search",
                new[] { "Observation" },
                new[]
                {
                    new KeyValuePair<string, string>("patient", "Patient/1"),
                    new KeyValuePair<string, string>("semantic-text", "near fainting?"),
                },
                CancellationToken.None);

            Assert.Equal("https://example.test/fhir/Observation?patient=Patient%2F1&semantic-text=near%20fainting%3F", handler.RequestUri!.AbsoluteUri);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "secret-token"), handler.Authorization);
            string requestCapture = await File.ReadAllTextAsync(Path.Combine(result.CaptureDirectory, "request.json"));
            Assert.DoesNotContain("Authorization", requestCapture, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-token", requestCapture, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(result.CaptureDirectory, "response.fhir.json")));
        }
        finally
        {
            if (Directory.Exists(captureRoot))
            {
                Directory.Delete(captureRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GivenNonFhirJsonResponse_WhenClientReadsIt_ThenValidationFailsAfterCapture()
    {
        string captureRoot = Path.Combine(Path.GetTempPath(), $"fhir-mcp-tests-{Guid.NewGuid():N}");
        try
        {
            var handler = new RecordingHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"not\":\"fhir\"}", Encoding.UTF8, "application/json"),
            });
            using var httpClient = new HttpClient(handler);
            var options = CreateOptions(captureRoot, maxCount: 10);
            var client = new FhirClient(options, httpClient, new FixedTokenProvider(token: null), new FhirCaptureWriter(options));

            FhirRequestException exception = await Assert.ThrowsAsync<FhirRequestException>(() =>
                client.GetAsync("invalid-response", new[] { "Observation" }, Array.Empty<KeyValuePair<string, string>>(), CancellationToken.None));

            Assert.Contains("resourceType", exception.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(exception.CaptureDirectory));
        }
        finally
        {
            if (Directory.Exists(captureRoot))
            {
                Directory.Delete(captureRoot, recursive: true);
            }
        }
    }

    private static FhirMcpOptions CreateOptions(string captureRoot, int maxCount) =>
        new(
            new Uri("https://example.test/fhir/"),
            tokenAddress: null,
            clientId: null,
            clientSecret: null,
            bearerToken: null,
            scope: "fhir-api",
            allowInsecureLocalhost: false,
            captureRoot,
            maxCount,
            maxResponseBytes: 1024 * 1024);

    private static HttpResponseMessage CreateFhirResponse(string resourceType) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($"{{\"resourceType\":\"{resourceType}\"}}", Encoding.UTF8, "application/fhir+json"),
        };

    private sealed class FixedTokenProvider : IFhirAccessTokenProvider
    {
        private readonly string? _token;

        internal FixedTokenProvider(string? token)
        {
            _token = token;
        }

        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(_token);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        internal RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        internal Uri? RequestUri { get; private set; }

        internal AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(_response);
        }
    }

    private sealed class RecordingFhirClient : IFhirClient
    {
        internal IReadOnlyList<string> PathSegments { get; private set; } = Array.Empty<string>();

        internal IReadOnlyList<KeyValuePair<string, string>> QueryParameters { get; private set; } = Array.Empty<KeyValuePair<string, string>>();

        internal string? RequestBody { get; private set; }

        public Task EnsureResourceTypeAsync(string resourceType, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<FhirResponse> GetAsync(
            string operationName,
            IReadOnlyList<string> pathSegments,
            IReadOnlyList<KeyValuePair<string, string>> queryParameters,
            CancellationToken cancellationToken)
        {
            PathSegments = pathSegments;
            QueryParameters = queryParameters;
            return Task.FromResult(CreateResponse("Bundle"));
        }

        public Task<FhirResponse> PostAsync(
            string operationName,
            IReadOnlyList<string> pathSegments,
            string requestBody,
            CancellationToken cancellationToken)
        {
            PathSegments = pathSegments;
            RequestBody = requestBody;
            return Task.FromResult(CreateResponse("Bundle"));
        }

        private static FhirResponse CreateResponse(string resourceType)
        {
            using JsonDocument document = JsonDocument.Parse($"{{\"resourceType\":\"{resourceType}\"}}");
            return new FhirResponse(200, new Uri("https://example.test/fhir"), document.RootElement.Clone(), "capture");
        }
    }
}
