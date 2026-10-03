// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark;

string endpoint = null;
string label = "run";
string manifestPath = null;
string outputPath = null;
string mode = "benchmark";
string ndjsonDirectory = null;
int parallelism = 32;
int iterations = 25;
int warmup = 5;
string adminClientId = "globalAdminServicePrincipal";
string caseFilter = null;
var explicitPatients = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--endpoint":
            endpoint = args[++i];
            break;
        case "--mode":
            mode = args[++i].ToLowerInvariant();
            break;
        case "--ndjson-dir":
            ndjsonDirectory = args[++i];
            break;
        case "--parallelism":
            parallelism = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--label":
            label = args[++i];
            break;
        case "--manifest":
            manifestPath = args[++i];
            break;
        case "--output":
            outputPath = args[++i];
            break;
        case "--iterations":
            iterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--warmup":
            warmup = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--admin-client":
            adminClientId = args[++i];
            break;
        case "--patient":
            explicitPatients.Add(args[++i]);
            break;
        case "--filter":
            caseFilter = args[++i];
            break;
        case "--help":
        case "-h":
            Console.WriteLine("Usage: FhirIncludeBenchmark --endpoint <url> [--mode benchmark|load]");
            Console.WriteLine("  benchmark: [--label name] [--manifest manifest.json] [--output results.json]");
            Console.WriteLine("             [--iterations N] [--warmup N] [--patient id]...");
            Console.WriteLine("  load     : --ndjson-dir <dir> [--parallelism N]");
            return 0;
        default:
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            return 1;
    }
}

if (string.IsNullOrWhiteSpace(endpoint))
{
    Console.Error.WriteLine("--endpoint is required.");
    return 1;
}

var baseUri = new Uri(endpoint.TrimEnd('/') + "/");
outputPath ??= $"benchmark-{label}.json";

using var handler = new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    MaxConnectionsPerServer = Math.Max(8, parallelism),
};

using var http = new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromMinutes(5), BaseAddress = baseUri };
var tokens = new TokenProvider(http, baseUri);

if (string.Equals(mode, "load", StringComparison.Ordinal))
{
    if (string.IsNullOrWhiteSpace(ndjsonDirectory))
    {
        Console.Error.WriteLine("--ndjson-dir is required for --mode load.");
        return 1;
    }

    Console.WriteLine($"Endpoint    : {baseUri}");
    Console.WriteLine($"NDJSON dir  : {ndjsonDirectory}");
    Console.WriteLine($"Parallelism : {parallelism}");
    Console.WriteLine();

    return await NdjsonLoader.RunAsync(http, tokens, adminClientId, ndjsonDirectory, parallelism);
}

// ── Resolve the patients to target ───────────────────────────────────────────────────────────────
// "heavy" patients have a 10x compartment and represent the worst case for include fan-out;
// "typical" patients represent normal traffic.
var targets = new List<(string PatientId, string Class)>();

if (explicitPatients.Count > 0)
{
    targets.AddRange(explicitPatients.Select(p => (p, "explicit")));
}
else if (!string.IsNullOrWhiteSpace(manifestPath))
{
    using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
    foreach (JsonElement id in manifest.RootElement.GetProperty("heavyPatientIds").EnumerateArray().Take(2))
    {
        targets.Add((id.GetString(), "heavy"));
    }

    foreach (JsonElement id in manifest.RootElement.GetProperty("typicalPatientIds").EnumerateArray().Take(1))
    {
        targets.Add((id.GetString(), "typical"));
    }
}
else
{
    Console.Error.WriteLine("Provide --manifest or at least one --patient.");
    return 1;
}

Console.WriteLine($"Endpoint   : {baseUri}");
Console.WriteLine($"Label      : {label}");
Console.WriteLine($"Iterations : {iterations} (+{warmup} warmup)");
Console.WriteLine($"Targets    : {string.Join(", ", targets.Select(t => $"{t.PatientId} ({t.Class})"))}");
Console.WriteLine();

var results = new List<CaseResult>();
var totalStopwatch = Stopwatch.StartNew();

IReadOnlyList<BenchmarkCase> cases = caseFilter == null
    ? QueryCatalog.Default
    : QueryCatalog.Default.Where(c => c.Name.Contains(caseFilter, StringComparison.OrdinalIgnoreCase)).ToList();

if (cases.Count == 0)
{
    Console.Error.WriteLine($"No cases matched filter '{caseFilter}'.");
    return 1;
}

foreach ((string patientId, string patientClass) in targets)
{
    foreach (BenchmarkCase testCase in cases)
    {
        string clientId = testCase.Auth == AuthMode.Admin ? adminClientId : patientId;
        string scope = testCase.Auth == AuthMode.Admin ? QueryCatalog.AdminScope : testCase.Scope;

        var result = new CaseResult
        {
            Name = testCase.Name,
            Group = testCase.Group,
            Auth = testCase.Auth.ToString(),
            PatientId = patientId,
            PatientClass = patientClass,
            PathAndQuery = testCase.PathAndQuery.Replace("{patient}", patientId, StringComparison.Ordinal),
            Notes = testCase.Notes,
            Iterations = iterations,
        };

        string token;
        try
        {
            token = await tokens.GetTokenAsync(clientId, scope, CancellationToken.None);
        }
        catch (Exception ex)
        {
            result.Errors = iterations;
            result.FirstError = "token: " + ex.Message;
            results.Add(result);
            Console.WriteLine($"  {result.Name,-38} {patientClass,-8} TOKEN FAILED: {ex.Message}");
            continue;
        }

        AuthenticationHeaderValue auth = TokenProvider.Bearer(token);
        var samples = new List<double>(iterations);

        for (int i = 0; i < warmup + iterations; i++)
        {
            bool measured = i >= warmup;
            try
            {
                (double elapsedMs, int entries, int matches, int includes, long bytes) =
                    await ExecuteAsync(http, auth, result.PathAndQuery, testCase.FollowRelatedLink);

                if (measured)
                {
                    samples.Add(elapsedMs);
                    result.EntryCount = entries;
                    result.MatchEntryCount = matches;
                    result.IncludeEntryCount = includes;
                    result.ResponseBytes = bytes;
                }
            }
            catch (Exception ex)
            {
                if (measured)
                {
                    result.Errors++;
                    result.FirstError ??= ex.Message;
                }
            }
        }

        if (samples.Count > 0)
        {
            samples.Sort();
            result.MeanMs = Math.Round(samples.Average(), 2);
            result.MinMs = Math.Round(samples[0], 2);
            result.P50Ms = Math.Round(Percentiles.Of(samples, 50), 2);
            result.P90Ms = Math.Round(Percentiles.Of(samples, 90), 2);
            result.P95Ms = Math.Round(Percentiles.Of(samples, 95), 2);
            result.P99Ms = Math.Round(Percentiles.Of(samples, 99), 2);
            result.MaxMs = Math.Round(samples[^1], 2);
        }

        results.Add(result);

        Console.WriteLine(
            $"  {result.Name,-38} {patientClass,-8} p50={result.P50Ms,8:N1}ms  p95={result.P95Ms,8:N1}ms  " +
            $"entries={result.EntryCount,5} (m={result.MatchEntryCount}/i={result.IncludeEntryCount})" +
            (result.Errors > 0 ? $"  ERRORS={result.Errors} ({result.FirstError})" : string.Empty));
    }
}

totalStopwatch.Stop();

var output = new
{
    label,
    endpoint = baseUri.ToString(),
    generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
    iterations,
    warmup,
    durationSeconds = Math.Round(totalStopwatch.Elapsed.TotalSeconds, 1),
    cases = results,
};

File.WriteAllText(outputPath, JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));

Console.WriteLine();
Console.WriteLine($"Completed {results.Count} cases in {totalStopwatch.Elapsed:hh\\:mm\\:ss}");
Console.WriteLine($"Results: {outputPath}");

int failed = results.Count(r => r.Errors > 0);
if (failed > 0)
{
    Console.WriteLine($"WARNING: {failed} case(s) had errors.");
}

return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────────────────────────
static async Task<(double ElapsedMs, int Entries, int Matches, int Includes, long Bytes)> ExecuteAsync(
    HttpClient http,
    AuthenticationHeaderValue auth,
    string pathAndQuery,
    bool followRelatedLink)
{
    string target = pathAndQuery;

    if (followRelatedLink)
    {
        // The initial search is NOT timed; only the $includes continuation it points at is.
        using var seed = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        seed.Headers.Authorization = auth;
        seed.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

        using HttpResponseMessage seedResponse = await http.SendAsync(seed);
        seedResponse.EnsureSuccessStatusCode();

        string seedBody = await seedResponse.Content.ReadAsStringAsync();
        string related = FindLink(seedBody, "related");

        if (string.IsNullOrEmpty(related))
        {
            throw new InvalidOperationException("No 'related' link was returned; increase the compartment size or lower _includesCount.");
        }

        target = related;
    }

    using var request = new HttpRequestMessage(HttpMethod.Get, target);
    request.Headers.Authorization = auth;
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/fhir+json"));

    var stopwatch = Stopwatch.StartNew();
    using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    string body = await response.Content.ReadAsStringAsync();
    stopwatch.Stop();

    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException(
            string.Format(CultureInfo.InvariantCulture, "{0}: {1}", (int)response.StatusCode, Truncate(body, 300)));
    }

    (int entries, int matches, int includes) = CountEntries(body);
    return (stopwatch.Elapsed.TotalMilliseconds, entries, matches, includes, body.Length);
}

static (int Entries, int Matches, int Includes) CountEntries(string bundleJson)
{
    using JsonDocument document = JsonDocument.Parse(bundleJson);

    if (!document.RootElement.TryGetProperty("entry", out JsonElement entry) ||
        entry.ValueKind != JsonValueKind.Array)
    {
        return (0, 0, 0);
    }

    int total = 0, matches = 0, includes = 0;
    foreach (JsonElement element in entry.EnumerateArray())
    {
        total++;
        if (element.TryGetProperty("search", out JsonElement search) &&
            search.TryGetProperty("mode", out JsonElement mode))
        {
            string modeValue = mode.GetString();
            if (string.Equals(modeValue, "match", StringComparison.Ordinal))
            {
                matches++;
            }
            else if (string.Equals(modeValue, "include", StringComparison.Ordinal))
            {
                includes++;
            }
        }
    }

    return (total, matches, includes);
}

static string FindLink(string bundleJson, string relation)
{
    using JsonDocument document = JsonDocument.Parse(bundleJson);

    if (!document.RootElement.TryGetProperty("link", out JsonElement link) ||
        link.ValueKind != JsonValueKind.Array)
    {
        return null;
    }

    foreach (JsonElement element in link.EnumerateArray())
    {
        if (element.TryGetProperty("relation", out JsonElement rel) &&
            string.Equals(rel.GetString(), relation, StringComparison.Ordinal) &&
            element.TryGetProperty("url", out JsonElement url))
        {
            return url.GetString();
        }
    }

    return null;
}

static string Truncate(string value, int max) =>
    string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];
