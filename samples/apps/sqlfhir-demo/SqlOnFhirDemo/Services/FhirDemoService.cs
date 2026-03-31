using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    /// Registers all three ViewDefinitions from the viewdefinitions/ folder.
    /// Returns registration status for each ViewDefinition.
    /// </summary>
    public async Task<List<ViewDefinitionRegistrationResult>> RegisterAllViewDefinitionsAsync(
        string viewDefinitionsPath,
        Action<string, string>? onProgress = null)
    {
        var results = new List<ViewDefinitionRegistrationResult>();

        string[] viewDefFiles = Directory.GetFiles(viewDefinitionsPath, "*.json");
        if (viewDefFiles.Length == 0)
        {
            _logger.LogWarning("No ViewDefinition files found in {Path}", viewDefinitionsPath);
            return results;
        }

        foreach (string filePath in viewDefFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            onProgress?.Invoke(fileName, "Registering...");

            try
            {
                string json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);
                string viewDefName = doc.RootElement.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? fileName
                    : fileName;
                string resourceType = doc.RootElement.TryGetProperty("resource", out var resProp)
                    ? resProp.GetString() ?? "Unknown"
                    : "Unknown";

                string response = await RegisterViewDefinitionAsync(json);
                bool success = !response.Contains("OperationOutcome", StringComparison.OrdinalIgnoreCase)
                    || !response.Contains("error", StringComparison.OrdinalIgnoreCase);

                results.Add(new ViewDefinitionRegistrationResult
                {
                    FileName = fileName,
                    ViewDefName = viewDefName,
                    ResourceType = resourceType,
                    Success = success,
                    Response = response,
                    ViewDefinitionJson = json,
                });

                onProgress?.Invoke(viewDefName, success ? "✓ Registered" : "✗ Failed");
            }
            catch (Exception ex)
            {
                results.Add(new ViewDefinitionRegistrationResult
                {
                    FileName = fileName,
                    ViewDefName = fileName,
                    Success = false,
                    Response = ex.Message,
                });
                onProgress?.Invoke(fileName, $"✗ Error: {ex.Message}");
            }
        }

        return results;
    }

    /// <summary>
    /// Gets the subscriptions for a specific ViewDefinition by searching for its criteria pattern.
    /// </summary>
    public async Task<string> GetSubscriptionForViewDefAsync(string resourceType)
    {
        var response = await _httpClient.GetAsync(
            $"Subscription?status=active,requested&criteria={resourceType}%3F&_format=json");
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Loads a Synthea-generated FHIR Bundle from a file, sanitizes it by removing
    /// external references that may fail (Practitioner, Organization, Location, etc.),
    /// and posts it to the FHIR server.
    /// </summary>
    public async Task<(bool Success, int ResourceCount)> LoadAndPostSyntheaBundleAsync(string filePath)
    {
        string rawJson = await File.ReadAllTextAsync(filePath);
        string sanitized = SanitizeSyntheaBundle(rawJson);

        var (success, _) = await PostBundleAsync(sanitized);

        // Count entries
        try
        {
            var doc = JsonNode.Parse(sanitized);
            int count = doc?["entry"]?.AsArray().Count ?? 0;
            return (success, count);
        }
        catch
        {
            return (success, 0);
        }
    }

    /// <summary>
    /// Loads multiple Synthea bundle files from a directory using parallel HTTP clients.
    /// Sanitizes each bundle and posts to the FHIR server with configurable concurrency.
    /// </summary>
    /// <param name="directory">Path to directory containing Synthea FHIR Bundle JSON files.</param>
    /// <param name="maxFiles">Maximum number of patient files to load (0 = all).</param>
    /// <param name="concurrency">Number of parallel upload threads.</param>
    /// <param name="onProgress">Callback reporting (filesLoaded, totalFiles, resourcesLoaded, failedFiles).</param>
    public async Task<(int FilesLoaded, int ResourcesLoaded, int Failed)> LoadSyntheaDirectoryAsync(
        string directory, int maxFiles = 0, int concurrency = 3,
        Action<int, int, int, int>? onProgress = null)
    {
        var files = Directory.GetFiles(directory, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("practitioner", StringComparison.OrdinalIgnoreCase)
                     && !Path.GetFileName(f).StartsWith("hospital", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (maxFiles > 0) files = files.Take(maxFiles).ToList();

        int filesLoaded = 0;
        int totalResources = 0;
        int failed = 0;
        int totalFiles = files.Count;

        var semaphore = new SemaphoreSlim(concurrency);
        var tasks = files.Select(async file =>
        {
            await semaphore.WaitAsync();
            try
            {
                var (success, count) = await LoadAndPostSyntheaBundleAsync(file);
                if (success)
                {
                    Interlocked.Increment(ref filesLoaded);
                    Interlocked.Add(ref totalResources, count);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                _logger.LogWarning(ex, "Failed to load bundle: {File}", Path.GetFileName(file));
            }
            finally
            {
                semaphore.Release();
                onProgress?.Invoke(
                    Volatile.Read(ref filesLoaded),
                    totalFiles,
                    Volatile.Read(ref totalResources),
                    Volatile.Read(ref failed));
            }
        });

        await Task.WhenAll(tasks);

        return (filesLoaded, totalResources, failed);
    }

    /// <summary>
    /// Sanitizes a Synthea-generated FHIR Bundle by:
    /// 1. Removing entries for resource types that cause reference failures (Practitioner, Organization, Location, etc.)
    /// 2. Stripping practitioner/organization/location references from remaining resources
    /// 3. Removing urn:uuid references that won't resolve on the server
    /// </summary>
    public static string SanitizeSyntheaBundle(string bundleJson)
    {
        var doc = JsonNode.Parse(bundleJson);
        if (doc == null) return bundleJson;

        var entries = doc["entry"]?.AsArray();
        if (entries == null) return bundleJson;

        // Resource types to keep (the ones our ViewDefinitions care about + supporting types)
        var keepTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Patient", "Observation", "Condition", "Encounter",
            "MedicationRequest", "Procedure", "AllergyIntolerance",
            "DiagnosticReport", "Immunization", "CarePlan", "CareTeam",
            "Claim", "ExplanationOfBenefit"
        };

        // Reference fields to strip (external references that may not resolve)
        var stripReferenceFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "practitioner", "provider", "organization", "managingOrganization",
            "serviceProvider", "insurer", "performer", "requester",
            "asserter", "recorder", "location"
        };

        var sanitizedEntries = new JsonArray();

        foreach (var entry in entries)
        {
            var resource = entry?["resource"];
            if (resource == null) continue;

            string? resourceType = resource["resourceType"]?.GetValue<string>();
            if (resourceType == null || !keepTypes.Contains(resourceType)) continue;

            // Strip problematic references from the resource
            StripReferences(resource, stripReferenceFields);

            // Rewrite urn:uuid references in the request URL to use resource type + server-assigned ID
            var request = entry?["request"];
            if (request != null)
            {
                string? url = request["url"]?.GetValue<string>();
                if (url != null)
                {
                    if (url.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Use PUT with resourceType/id instead of POST with urn:uuid
                        string? id = resource["id"]?.GetValue<string>();
                        if (id != null)
                        {
                            request["method"] = "PUT";
                            request["url"] = $"{resourceType}/{id}";
                        }
                    }
                    else if (IsStrippableReference(url))
                    {
                        // Skip entries whose request URL targets a stripped resource type
                        // (e.g., "Practitioner?identifier=..." conditional creates)
                        continue;
                    }
                }
            }

            sanitizedEntries.Add(entry!.DeepClone());
        }

        doc["entry"] = sanitizedEntries;
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static void StripReferences(JsonNode resource, HashSet<string> fieldsToStrip)
    {
        if (resource is not JsonObject obj) return;

        var keysToRemove = new List<string>();

        foreach (var kvp in obj)
        {
            // Strip direct reference fields
            if (fieldsToStrip.Contains(kvp.Key))
            {
                keysToRemove.Add(kvp.Key);
                continue;
            }

            // Strip any nested "reference" values that point to urn:uuid or stripped types
            if (kvp.Value is JsonObject nested)
            {
                var refValue = nested["reference"]?.GetValue<string>();
                if (refValue != null && IsStrippableReference(refValue))
                {
                    keysToRemove.Add(kvp.Key);
                    continue;
                }

                StripReferences(nested, fieldsToStrip);
            }
            else if (kvp.Value is JsonArray arr)
            {
                // Process arrays (e.g., performer[])
                var itemsToRemove = new List<int>();
                for (int i = 0; i < arr.Count; i++)
                {
                    if (arr[i] is JsonObject arrItem)
                    {
                        var refVal = arrItem["reference"]?.GetValue<string>();
                        if (refVal != null && IsStrippableReference(refVal))
                        {
                            itemsToRemove.Add(i);
                        }
                        else
                        {
                            StripReferences(arrItem, fieldsToStrip);
                        }
                    }
                }

                // Remove items in reverse order to preserve indices
                foreach (int idx in itemsToRemove.OrderByDescending(x => x))
                {
                    arr.RemoveAt(idx);
                }
            }
        }

        foreach (var key in keysToRemove)
        {
            obj.Remove(key);
        }
    }

    private static bool IsStrippableReference(string reference)
    {
        return reference.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Practitioner/", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Practitioner?", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Organization/", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Organization?", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Location/", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("Location?", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("PractitionerRole/", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("PractitionerRole?", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resets the demo by deleting all clinical resources, subscriptions, and ViewDefinitions.
    /// Uses FHIR conditional delete ($everything-style) for each resource type.
    /// </summary>
    public async Task<string> ResetDemoAsync(Action<string>? onProgress = null)
    {
        var results = new StringBuilder();

        // Resource types to delete in dependency order (children before parents).
        // Library is included to clean up persisted ViewDefinition registrations,
        // which triggers the cleanup behavior to drop materialized SQL tables.
        string[] resourceTypes = { "Observation", "Condition", "Encounter", "Patient", "Subscription", "Library" };

        foreach (string resourceType in resourceTypes)
        {
            onProgress?.Invoke($"Deleting {resourceType} resources...");
            try
            {
                // Use conditional delete to delete all resources of this type
                // FHIR spec: DELETE [base]/[type]?[search parameters] with _hardDelete for clean removal
                var response = await _httpClient.DeleteAsync($"{resourceType}?_hardDelete=true&_count=100");
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    results.AppendLine($"✓ {resourceType}: deleted");
                    _logger.LogInformation("Reset: deleted {ResourceType} resources", resourceType);
                }
                else
                {
                    results.AppendLine($"⚠ {resourceType}: {response.StatusCode}");
                    _logger.LogWarning("Reset: failed to delete {ResourceType}: {Status}", resourceType, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                results.AppendLine($"✗ {resourceType}: {ex.Message}");
                _logger.LogWarning(ex, "Reset: error deleting {ResourceType}", resourceType);
            }
        }

        onProgress?.Invoke("Done!");
        return results.ToString();
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

    private static readonly string[] CrisisLastNames = { "Pressmore", "Dangerfield", "Redline", "Vasquez", "Thornton", "Okafor", "Chen", "Kowalski", "Baptiste", "Nakamura", "Rivera", "Petrov", "Johansson", "Abadi", "Fitzgerald", "Moreau", "Tanaka", "Blackwell", "Gutierrez", "Andersen" };
    private static readonly string[] CrisisFirstNames = { "Hyper", "Rod", "Scarlett", "Maria", "James", "Chidi", "Wei", "Stefan", "Marie", "Kenji", "Carlos", "Dmitri", "Erik", "Farhan", "Sean", "Isabelle", "Yuki", "Marcus", "Elena", "Lars" };
    private static readonly Random Rng = new();

    /// <summary>
    /// Generates a batch of crisis patients with hypertension and severely uncontrolled blood pressure.
    /// Posts them in bundles of 50 for efficiency.
    /// </summary>
    /// <param name="count">Number of crisis patients to generate.</param>
    /// <param name="onProgress">Callback reporting (patientsCreated, totalPatients).</param>
    public async Task<int> GenerateCrisisPatientsAsync(int count, Action<int, int>? onProgress = null)
    {
        int created = 0;
        int batchSize = 50;

        for (int batchStart = 0; batchStart < count; batchStart += batchSize)
        {
            int batchEnd = Math.Min(batchStart + batchSize, count);
            var entries = new StringBuilder();

            for (int i = batchStart; i < batchEnd; i++)
            {
                string id = $"crisis-gen-{i:D4}";
                string lastName = CrisisLastNames[i % CrisisLastNames.Length];
                string firstName = CrisisFirstNames[i % CrisisFirstNames.Length];
                string gender = i % 2 == 0 ? "male" : "female";
                int birthYear = 1945 + Rng.Next(40); // Ages 41-81
                int systolic = 145 + Rng.Next(50);   // 145-194
                int diastolic = 92 + Rng.Next(30);   // 92-121
                string now = DateTime.UtcNow.AddMinutes(i).ToString("yyyy-MM-ddTHH:mm:ssZ");

                if (entries.Length > 0) entries.Append(",");

                // Patient
                entries.Append($@"
                {{""resource"": {{""resourceType"": ""Patient"", ""id"": ""{id}"", ""name"": [{{""use"": ""official"", ""family"": ""{lastName}"", ""given"": [""{firstName}""]}}], ""gender"": ""{gender}"", ""birthDate"": ""{birthYear}-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}""}}, ""request"": {{""method"": ""PUT"", ""url"": ""Patient/{id}""}}}},");

                // Hypertension condition
                entries.Append($@"
                {{""resource"": {{""resourceType"": ""Condition"", ""id"": ""{id}-htn"", ""subject"": {{""reference"": ""Patient/{id}""}}, ""code"": {{""coding"": [{{""system"": ""http://snomed.info/sct"", ""code"": ""59621000"", ""display"": ""Essential hypertension""}}]}}, ""clinicalStatus"": {{""coding"": [{{""system"": ""http://terminology.hl7.org/CodeSystem/condition-clinical"", ""code"": ""active""}}]}}, ""verificationStatus"": {{""coding"": [{{""system"": ""http://terminology.hl7.org/CodeSystem/condition-ver-status"", ""code"": ""confirmed""}}]}}}}, ""request"": {{""method"": ""PUT"", ""url"": ""Condition/{id}-htn""}}}},");

                // Uncontrolled BP observation
                entries.Append($@"
                {{""resource"": {{""resourceType"": ""Observation"", ""id"": ""{id}-bp"", ""status"": ""final"", ""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""85354-9"", ""display"": ""Blood pressure panel""}}]}}, ""subject"": {{""reference"": ""Patient/{id}""}}, ""effectiveDateTime"": ""{now}"", ""component"": [{{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8480-6"", ""display"": ""Systolic BP""}}]}}, ""valueQuantity"": {{""value"": {systolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}, {{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8462-4"", ""display"": ""Diastolic BP""}}]}}, ""valueQuantity"": {{""value"": {diastolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}]}}}}, ""request"": {{""method"": ""PUT"", ""url"": ""Observation/{id}-bp""}}}}");
            }

            string bundle = $@"{{""resourceType"": ""Bundle"", ""type"": ""batch"", ""entry"": [{entries}]}}";
            var (success, _) = await PostBundleAsync(bundle);

            if (success) created += (batchEnd - batchStart);
            onProgress?.Invoke(created, count);

            _logger.LogInformation("Crisis patients batch: {Created}/{Total}", created, count);
        }

        return created;
    }

    /// <summary>
    /// Generates intervention observations (corrected BPs) for previously created crisis patients.
    /// Only corrects a percentage of patients (default 60%) to show realistic partial recovery.
    /// </summary>
    /// <param name="crisisCount">Total crisis patients that were generated.</param>
    /// <param name="correctionRate">Fraction of patients to correct (0.0-1.0).</param>
    /// <param name="onProgress">Callback reporting (corrected, total).</param>
    public async Task<int> GenerateInterventionsAsync(int crisisCount, double correctionRate = 0.6, Action<int, int>? onProgress = null)
    {
        int toCorrect = (int)(crisisCount * correctionRate);
        int corrected = 0;
        int batchSize = 50;

        for (int batchStart = 0; batchStart < toCorrect; batchStart += batchSize)
        {
            int batchEnd = Math.Min(batchStart + batchSize, toCorrect);
            var entries = new StringBuilder();

            for (int i = batchStart; i < batchEnd; i++)
            {
                string patientId = $"crisis-gen-{i:D4}";
                string obsId = $"intervention-gen-{i:D4}";
                int systolic = 115 + Rng.Next(23);   // 115-137 (controlled)
                int diastolic = 68 + Rng.Next(20);   // 68-87 (controlled)
                string now = DateTime.UtcNow.AddMinutes(i).ToString("yyyy-MM-ddTHH:mm:ssZ");

                if (entries.Length > 0) entries.Append(",");

                entries.Append($@"
                {{""resource"": {{""resourceType"": ""Observation"", ""id"": ""{obsId}"", ""status"": ""final"", ""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""85354-9"", ""display"": ""Blood pressure panel""}}]}}, ""subject"": {{""reference"": ""Patient/{patientId}""}}, ""effectiveDateTime"": ""{now}"", ""component"": [{{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8480-6"", ""display"": ""Systolic BP""}}]}}, ""valueQuantity"": {{""value"": {systolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}, {{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8462-4"", ""display"": ""Diastolic BP""}}]}}, ""valueQuantity"": {{""value"": {diastolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}]}}}}, ""request"": {{""method"": ""PUT"", ""url"": ""Observation/{obsId}""}}}}");
            }

            string bundle = $@"{{""resourceType"": ""Bundle"", ""type"": ""batch"", ""entry"": [{entries}]}}";
            var (success, _) = await PostBundleAsync(bundle);

            if (success) corrected += (batchEnd - batchStart);
            onProgress?.Invoke(corrected, toCorrect);

            _logger.LogInformation("Interventions batch: {Corrected}/{Total}", corrected, toCorrect);
        }

        return corrected;
    }
}

/// <summary>
/// Result of registering a single ViewDefinition.
/// </summary>
public class ViewDefinitionRegistrationResult
{
    public string FileName { get; set; } = "";
    public string ViewDefName { get; set; } = "";
    public string ResourceType { get; set; } = "Unknown";
    public bool Success { get; set; }
    public string Response { get; set; } = "";
    public string ViewDefinitionJson { get; set; } = "";
}
