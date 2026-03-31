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
    /// <summary>
    /// Tag applied to all resources created by the demo app, enabling targeted bulk delete on reset.
    /// </summary>
    public const string DemoTag = "sqlonfhirdemo";

    private const string DemoTagSystem = "https://sql-on-fhir.org/demo";

    /// <summary>
    /// JSON fragment for the meta.tag to inject into resources.
    /// </summary>
    private const string DemoMetaTagJson = @"""meta"": {""tag"": [{""system"": ""https://sql-on-fhir.org/demo"", ""code"": ""sqlonfhirdemo""}]}";

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
    /// Registers a ViewDefinition for materialization by creating a Library resource
    /// with the ViewDefinition profile. The FHIR server intercepts Library creation and
    /// triggers materialization (SQL table, population job, subscription).
    /// </summary>
    public async Task<string> RegisterViewDefinitionAsync(string viewDefinitionJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(viewDefinitionJson);
        string name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "unknown" : "unknown";
        string resource = doc.RootElement.TryGetProperty("resource", out var r) ? r.GetString() ?? "Unknown" : "Unknown";

        string base64Content = Convert.ToBase64String(Encoding.UTF8.GetBytes(viewDefinitionJson));

        string libraryJson = $$"""
            {
                "resourceType": "Library",
                "meta": {
                    "profile": ["{{ViewDefinitionLibraryProfile}}"],
                    "tag": [{"system": "{{DemoTagSystem}}", "code": "{{DemoTag}}"}]
                },
                "name": "{{name}}",
                "title": "ViewDefinition: {{name}}",
                "status": "active",
                "type": {
                    "coding": [{"system": "http://terminology.hl7.org/CodeSystem/library-type", "code": "logic-library"}]
                },
                "description": "SQL on FHIR v2 ViewDefinition for {{resource}} resources.",
                "content": [{
                    "contentType": "application/json+viewdefinition",
                    "data": "{{base64Content}}"
                }]
            }
            """;

        var content = new StringContent(libraryJson, Encoding.UTF8, "application/fhir+json");
        var response = await _httpClient.PostAsync("Library", content);
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
    /// Posts a FHIR Bundle to the server with parallel processing enabled.
    /// </summary>
    public async Task<(bool Success, string Response)> PostBundleAsync(string bundleJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "");
        request.Content = new StringContent(bundleJson, Encoding.UTF8, "application/fhir+json");
        request.Headers.Add("x-bundle-processing-logic", "Parallel");

        var response = await _httpClient.SendAsync(request);
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
    /// Queries the materialization status of a registered ViewDefinition.
    /// Returns the status JSON from GET ViewDefinition/{name}.
    /// </summary>
    public async Task<ViewDefinitionMaterializationStatus?> GetViewDefinitionStatusAsync(string viewDefName)
    {
        var response = await _httpClient.GetAsync($"ViewDefinition/{viewDefName}");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<ViewDefinitionMaterializationStatus>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
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
        string directory, int maxFiles = 0, int skip = 0, int concurrency = 3,
        Action<int, int, int, int>? onProgress = null)
    {
        // Step 1: Load prerequisite bundles first (practitioners, hospitals/organizations).
        // These contain Practitioner, PractitionerRole, Organization, and Location resources
        // that patient bundles reference via conditional references.
        var prerequisiteFiles = Directory.GetFiles(directory, "*.json")
            .Where(f => Path.GetFileName(f).StartsWith("practitioner", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(f).StartsWith("hospital", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (string prereqFile in prerequisiteFiles)
        {
            try
            {
                _logger.LogInformation("Loading prerequisite bundle: {File}", Path.GetFileName(prereqFile));
                await LoadAndPostSyntheaBundleAsync(prereqFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load prerequisite: {File}", Path.GetFileName(prereqFile));
            }
        }

        // Step 2: Load patient bundles (skip previously loaded files)
        var files = Directory.GetFiles(directory, "*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("practitioner", StringComparison.OrdinalIgnoreCase)
                     && !Path.GetFileName(f).StartsWith("hospital", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .Skip(skip)
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
    /// 1. Converting from transaction to batch (entries processed independently, partial failures OK)
    /// 2. Rewriting urn:uuid request URLs to PUT with resource type/id
    /// 3. Injecting demo tag for targeted bulk delete
    /// References are kept intact — in batch mode, entries that fail (e.g., dangling Practitioner
    /// references) simply return individual errors without affecting other entries.
    /// </summary>
    public static string SanitizeSyntheaBundle(string bundleJson)
    {
        var doc = JsonNode.Parse(bundleJson);
        if (doc == null) return bundleJson;

        // Convert transaction to batch — each entry processes independently, so reference
        // failures don't roll back the whole bundle. This also avoids a NullReferenceException
        // in CreateResourceHandler.IsBundleParallelTransaction for transaction bundles.
        doc["type"] = "batch";

        var entries = doc["entry"]?.AsArray();
        if (entries == null) return bundleJson;

        var sanitizedEntries = new JsonArray();

        foreach (var entry in entries)
        {
            var resource = entry?["resource"];
            if (resource == null) continue;

            string? resourceType = resource["resourceType"]?.GetValue<string>();
            if (resourceType == null) continue;

            // Rewrite urn:uuid request URLs to PUT with explicit resource type/id
            var request = entry?["request"];
            if (request != null)
            {
                string? url = request["url"]?.GetValue<string>();
                if (url != null && url.StartsWith("urn:uuid:", StringComparison.OrdinalIgnoreCase))
                {
                    string? id = resource["id"]?.GetValue<string>();
                    if (id != null)
                    {
                        request["method"] = "PUT";
                        request["url"] = $"{resourceType}/{id}";
                    }
                }
            }

            // Inject demo tag into resource meta for targeted bulk delete
            InjectDemoTag(resource);

            sanitizedEntries.Add(entry!.DeepClone());
        }

        doc["entry"] = sanitizedEntries;
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// Injects the demo tag into a resource's meta.tag array for targeted bulk delete.
    /// </summary>
    private static void InjectDemoTag(JsonNode resource)
    {
        if (resource is not JsonObject obj)
        {
            return;
        }

        var tagNode = new JsonObject
        {
            ["system"] = DemoTagSystem,
            ["code"] = DemoTag,
        };

        if (obj["meta"] is JsonObject meta)
        {
            if (meta["tag"] is JsonArray tagArray)
            {
                tagArray.Add(tagNode);
            }
            else
            {
                meta["tag"] = new JsonArray(tagNode);
            }
        }
        else
        {
            obj["meta"] = new JsonObject
            {
                ["tag"] = new JsonArray(tagNode),
            };
        }
    }

    /// <summary>
    /// Resets the demo by unregistering ViewDefinitions (which drops materialized tables and subscriptions),
    /// then bulk-deleting all tagged demo resources.
    /// </summary>
    public async Task<string> ResetDemoAsync(Action<string>? onProgress = null)
    {
        var results = new StringBuilder();

        // Step 1: Unregister each ViewDefinition (drops SQL tables + deletes subscriptions)
        string[] viewDefNames = { "patient_demographics", "us_core_blood_pressures", "condition_flat" };
        foreach (string viewDefName in viewDefNames)
        {
            onProgress?.Invoke($"Unregistering ViewDefinition: {viewDefName}...");
            try
            {
                // Delete the Library resource, which triggers ViewDefinitionLibraryCleanupBehavior
                // to call UnregisterAsync(dropTable: true)
                var searchResponse = await _httpClient.GetAsync(
                    $"Library?name={viewDefName}&_profile={Uri.EscapeDataString(ViewDefinitionLibraryProfile)}&_format=json");

                if (searchResponse.IsSuccessStatusCode)
                {
                    string searchJson = await searchResponse.Content.ReadAsStringAsync();
                    var searchDoc = JsonNode.Parse(searchJson);
                    var entries = searchDoc?["entry"]?.AsArray();

                    if (entries != null)
                    {
                        foreach (var entry in entries)
                        {
                            string? libraryId = entry?["resource"]?["id"]?.GetValue<string>();
                            if (libraryId != null)
                            {
                                await _httpClient.DeleteAsync($"Library/{libraryId}?_hardDelete=true");
                                results.AppendLine($"✓ ViewDef '{viewDefName}': Library/{libraryId} deleted");
                            }
                        }
                    }
                    else
                    {
                        results.AppendLine($"⚠ ViewDef '{viewDefName}': no Library resource found");
                    }
                }
            }
            catch (Exception ex)
            {
                results.AppendLine($"✗ ViewDef '{viewDefName}': {ex.Message}");
                _logger.LogWarning(ex, "Reset: error unregistering ViewDef {ViewDefName}", viewDefName);
            }
        }

        // Step 2: Verify subscriptions are gone
        onProgress?.Invoke("Verifying subscriptions cleaned up...");
        try
        {
            var subResponse = await _httpClient.GetAsync("Subscription?status=active,requested&_format=json&_count=10");
            string subJson = await subResponse.Content.ReadAsStringAsync();
            var subDoc = JsonNode.Parse(subJson);
            int? subTotal = subDoc?["total"]?.GetValue<int>();
            results.AppendLine($"✓ Active subscriptions remaining: {subTotal ?? 0}");
        }
        catch (Exception ex)
        {
            results.AppendLine($"⚠ Could not verify subscriptions: {ex.Message}");
        }

        // Step 3: Bulk delete all tagged demo resources (async operation requires Prefer: respond-async)
        onProgress?.Invoke("Bulk deleting tagged demo resources...");
        try
        {
            string tagFilter = $"{DemoTagSystem}|{DemoTag}";
            var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"$bulk-delete?_tag={Uri.EscapeDataString(tagFilter)}&_hardDelete=true&_purgeHistory=true");
            request.Headers.Add("Prefer", "respond-async");

            var deleteResponse = await _httpClient.SendAsync(request);
            string deleteBody = await deleteResponse.Content.ReadAsStringAsync();

            if (deleteResponse.IsSuccessStatusCode)
            {
                results.AppendLine($"✓ Bulk delete: job accepted ({deleteResponse.StatusCode})");
                _logger.LogInformation("Reset: bulk delete of tagged resources accepted");
            }
            else
            {
                results.AppendLine($"⚠ Bulk delete: {deleteResponse.StatusCode} — {deleteBody}");
                _logger.LogWarning("Reset: bulk delete returned {Status}", deleteResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            results.AppendLine($"✗ Bulk delete: {ex.Message}");
            _logger.LogWarning(ex, "Reset: bulk delete failed");
        }

        onProgress?.Invoke("Done!");
        return results.ToString();
    }

    /// <summary>
    /// Profile URL for ViewDefinition Library resources (used in reset search).
    /// </summary>
    private const string ViewDefinitionLibraryProfile = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition";

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
                {{""resource"": {{""resourceType"": ""Patient"", ""id"": ""{id}"", {DemoMetaTagJson}, ""name"": [{{""use"": ""official"", ""family"": ""{lastName}"", ""given"": [""{firstName}""]}}], ""gender"": ""{gender}"", ""birthDate"": ""{birthYear}-{(i % 12) + 1:D2}-{(i % 28) + 1:D2}""}}, ""request"": {{""method"": ""PUT"", ""url"": ""Patient/{id}""}}}},");

                // Hypertension condition
                entries.Append($@"
                {{""resource"": {{""resourceType"": ""Condition"", ""id"": ""{id}-htn"", {DemoMetaTagJson}, ""subject"": {{""reference"": ""Patient/{id}""}}, ""code"": {{""coding"": [{{""system"": ""http://snomed.info/sct"", ""code"": ""59621000"", ""display"": ""Essential hypertension""}}]}}, ""clinicalStatus"": {{""coding"": [{{""system"": ""http://terminology.hl7.org/CodeSystem/condition-clinical"", ""code"": ""active""}}]}}, ""verificationStatus"": {{""coding"": [{{""system"": ""http://terminology.hl7.org/CodeSystem/condition-ver-status"", ""code"": ""confirmed""}}]}}}}, ""request"": {{""method"": ""PUT"", ""url"": ""Condition/{id}-htn""}}}},");

                // Uncontrolled BP observation
                entries.Append($@"
                {{""resource"": {{""resourceType"": ""Observation"", ""id"": ""{id}-bp"", {DemoMetaTagJson}, ""status"": ""final"", ""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""85354-9"", ""display"": ""Blood pressure panel""}}]}}, ""subject"": {{""reference"": ""Patient/{id}""}}, ""effectiveDateTime"": ""{now}"", ""component"": [{{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8480-6"", ""display"": ""Systolic BP""}}]}}, ""valueQuantity"": {{""value"": {systolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}, {{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8462-4"", ""display"": ""Diastolic BP""}}]}}, ""valueQuantity"": {{""value"": {diastolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}]}}}}, ""request"": {{""method"": ""PUT"", ""url"": ""Observation/{id}-bp""}}}}");
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
                {{""resource"": {{""resourceType"": ""Observation"", ""id"": ""{obsId}"", {DemoMetaTagJson}, ""status"": ""final"", ""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""85354-9"", ""display"": ""Blood pressure panel""}}]}}, ""subject"": {{""reference"": ""Patient/{patientId}""}}, ""effectiveDateTime"": ""{now}"", ""component"": [{{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8480-6"", ""display"": ""Systolic BP""}}]}}, ""valueQuantity"": {{""value"": {systolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}, {{""code"": {{""coding"": [{{""system"": ""http://loinc.org"", ""code"": ""8462-4"", ""display"": ""Diastolic BP""}}]}}, ""valueQuantity"": {{""value"": {diastolic}, ""unit"": ""mmHg"", ""system"": ""http://unitsofmeasure.org"", ""code"": ""mm[Hg]""}}}}]}}}}, ""request"": {{""method"": ""PUT"", ""url"": ""Observation/{obsId}""}}}}");
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

/// <summary>
/// Materialization status returned by GET ViewDefinition/{name}.
/// </summary>
public class ViewDefinitionMaterializationStatus
{
    public string ViewDefinitionName { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? ErrorMessage { get; set; }
    public List<string> SubscriptionIds { get; set; } = new();
    public string? LibraryResourceId { get; set; }
    public DateTimeOffset? RegisteredAt { get; set; }
    public bool TableExists { get; set; }
}
