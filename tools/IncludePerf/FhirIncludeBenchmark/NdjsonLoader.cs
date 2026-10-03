// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.Health.Internal.Fhir.IncludePerf.Benchmark
{
    /// <summary>
    /// Loads generated NDJSON into a FHIR server over the REST API using parallel conditional-free PUTs.
    /// <para>
    /// This exists for local validation and small datasets. For the multi-million resource runs, use
    /// $import (bulk load) instead - see Invoke-IncludePerfABTest.ps1.
    /// </para>
    /// </summary>
    internal static class NdjsonLoader
    {
        internal static async Task<int> RunAsync(
            HttpClient http,
            TokenProvider tokens,
            string clientId,
            string ndjsonDirectory,
            int parallelism)
        {
            string token = await tokens.GetTokenAsync(clientId, QueryCatalog.AdminScope, CancellationToken.None);
            AuthenticationHeaderValue auth = TokenProvider.Bearer(token);

            // Resource types are loaded in dependency order only for readability; the server does not
            // require referenced resources to exist.
            string[] files = Directory.GetFiles(ndjsonDirectory, "*.ndjson").OrderBy(f => f, StringComparer.Ordinal).ToArray();

            long total = 0;
            long failed = 0;
            var errors = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
            var stopwatch = Stopwatch.StartNew();

            foreach (string file in files)
            {
                var lines = new List<string>();
                foreach (string line in File.ReadLines(file))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        lines.Add(line);
                    }
                }

                await Parallel.ForEachAsync(
                    lines,
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                    async (line, cancellationToken) =>
                    {
                        try
                        {
                            using JsonDocument document = JsonDocument.Parse(line);
                            string resourceType = document.RootElement.GetProperty("resourceType").GetString();
                            string id = document.RootElement.GetProperty("id").GetString();

                            using var request = new HttpRequestMessage(HttpMethod.Put, $"{resourceType}/{id}")
                            {
                                Content = new StringContent(line, Encoding.UTF8, "application/fhir+json"),
                            };
                            request.Headers.Authorization = auth;

                            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);

                            if (!response.IsSuccessStatusCode)
                            {
                                Interlocked.Increment(ref failed);
                                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                                errors.AddOrUpdate(
                                    $"{(int)response.StatusCode} {resourceType}: {Truncate(body, 200)}",
                                    1,
                                    (_, count) => count + 1);
                            }
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref failed);
                            errors.AddOrUpdate(ex.Message, 1, (_, count) => count + 1);
                        }

                        long done = Interlocked.Increment(ref total);
                        if (done % 5000 == 0)
                        {
                            Console.WriteLine($"  {done:N0} loaded ({failed:N0} failed)  [{stopwatch.Elapsed:hh\\:mm\\:ss}]");
                        }
                    });
            }

            stopwatch.Stop();
            Console.WriteLine();
            Console.WriteLine($"Loaded {total - failed:N0}/{total:N0} resources in {stopwatch.Elapsed:hh\\:mm\\:ss}");

            if (!errors.IsEmpty)
            {
                Console.WriteLine();
                Console.WriteLine("Top errors:");
                foreach (var kvp in errors.OrderByDescending(e => e.Value).Take(10))
                {
                    Console.WriteLine($"  [{kvp.Value,6}] {kvp.Key}");
                }
            }

            return failed == 0 ? 0 : 1;
        }

        private static string Truncate(string value, int max) =>
            string.IsNullOrEmpty(value) || value.Length <= max
                ? value
                : value.Substring(0, max).Replace('\n', ' ').Replace('\r', ' ');
    }
}
