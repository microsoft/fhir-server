using System.Text;
using System.Text.Json;

namespace SqlOnFhirDemo.Services;

/// <summary>
/// Service for interacting with the FHIR server from the demo app.
/// Handles ViewDefinition registration, data loading, and querying materialized views.
/// </summary>
public class FhirDemoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FhirDemoService> _logger;

    public FhirDemoService(HttpClient httpClient, ILogger<FhirDemoService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the FHIR server base URL.
    /// </summary>
    public string BaseUrl => _httpClient.BaseAddress?.ToString() ?? "http://localhost:44348";

    /// <summary>
    /// Registers a ViewDefinition for materialization by posting it via the $run endpoint
    /// with a materialize parameter.
    /// </summary>
    public async Task<string> RegisterViewDefinitionAsync(string viewDefinitionJson)
    {
        var content = new StringContent(viewDefinitionJson, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("ViewDefinition/$run", content);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Queries a materialized ViewDefinition via the $run endpoint.
    /// </summary>
    public async Task<List<Dictionary<string, object?>>> QueryViewDefinitionAsync(string viewDefName, string format = "json")
    {
        var response = await _httpClient.GetAsync($"ViewDefinition/{viewDefName}/$run?_format={format}");
        var json = await response.Content.ReadAsStringAsync();

        if (response.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                return JsonSerializer.Deserialize<List<Dictionary<string, object?>>>(json) ?? new();
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse ViewDefinition query response as JSON");
            }
        }

        return new();
    }

    /// <summary>
    /// Posts a FHIR Bundle to the server.
    /// </summary>
    public async Task<(bool Success, string Response)> PostBundleAsync(string bundleJson)
    {
        var content = new StringContent(bundleJson, Encoding.UTF8, "application/fhir+json");
        var response = await _httpClient.PostAsync("", content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return (response.IsSuccessStatusCode, responseBody);
    }

    /// <summary>
    /// Creates a single FHIR resource.
    /// </summary>
    public async Task<(bool Success, string Response)> CreateResourceAsync(string resourceType, string resourceJson)
    {
        var content = new StringContent(resourceJson, Encoding.UTF8, "application/fhir+json");
        var response = await _httpClient.PostAsync(resourceType, content);
        var responseBody = await response.Content.ReadAsStringAsync();
        return (response.IsSuccessStatusCode, responseBody);
    }

    /// <summary>
    /// Records a blood pressure observation for a patient.
    /// </summary>
    public async Task<(bool Success, string Response)> RecordBloodPressureAsync(string patientReference, int systolic, int diastolic)
    {
        string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        string json = $@"{{
            ""resourceType"": ""Observation"",
            ""status"": ""final"",
            ""code"": {{
                ""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""85354-9"", ""display"": ""Blood pressure panel""}}]
            }},
            ""subject"": {{""reference"": ""{patientReference}""}},
            ""effectiveDateTime"": ""{now}"",
            ""component"": [
                {{
                    ""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8480-6"", ""display"": ""Systolic BP""}}]}},
                    ""valueQuantity"": {{""value"": {systolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}
                }},
                {{
                    ""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8462-4"", ""display"": ""Diastolic BP""}}]}},
                    ""valueQuantity"": {{""value"": {diastolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}
                }}
            ]
        }}";

        return await CreateResourceAsync("Observation", json);
    }

    /// <summary>
    /// Searches for Subscription resources to show auto-created subscriptions.
    /// </summary>
    public async Task<string> GetSubscriptionsAsync()
    {
        var response = await _httpClient.GetAsync("Subscription?status=active,requested&_format=json");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Gets the FHIR server metadata.
    /// </summary>
    public async Task<bool> CheckServerHealthAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync("metadata");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
