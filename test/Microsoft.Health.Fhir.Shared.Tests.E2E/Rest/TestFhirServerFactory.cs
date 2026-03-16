// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Creates and caches <see cref="TestFhirServer"/> instances. This class is intended to be used as an assembly fixture,
    /// so that the <see cref="TestFhirServer"/> instances can be reused across test classes in an assembly.
    /// </summary>
    /// <remarks>
    /// This factory supports test retries (e.g., via dotnet test --retry-failed-tests).
    /// When a fixture initialization fails, the failure is tracked and the cache entry is cleared,
    /// allowing subsequent retry attempts to create a fresh instance.
    /// </remarks>
    public class TestFhirServerFactory : IAsyncLifetime, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<(DataStore dataStore, Type startupType), Lazy<Task<TestFhirServer>>> _cache = new ConcurrentDictionary<(DataStore dataStore, Type startupType), Lazy<Task<TestFhirServer>>>();

        /// <summary>
        /// Tracks fixture initialization failures to support test retry scenarios.
        /// When --retry-failed-tests is used, failed fixtures need to be re-created.
        /// </summary>
        private readonly ConcurrentDictionary<(DataStore dataStore, Type startupType), Exception> _failedInitializations = new ConcurrentDictionary<(DataStore dataStore, Type startupType), Exception>();

        public async Task<TestFhirServer> GetTestFhirServerAsync(DataStore dataStore, Type startupType)
        {
            var key = (dataStore, startupType);

            // Check if this is a retry after a previous initialization failure
            // If so, clear the cached failure so we can attempt a fresh initialization
            if (_failedInitializations.TryRemove(key, out var previousFailure))
            {
                Console.WriteLine($"[TestFhirServerFactory] Previous initialization for {dataStore}/{startupType?.Name} failed with: {previousFailure.Message}");
                Console.WriteLine($"[TestFhirServerFactory] Clearing cache to allow retry with fresh initialization...");

                // Remove the failed Lazy from the cache so GetOrAdd creates a new one
                _cache.TryRemove(key, out _);
            }

            try
            {
                return await _cache.GetOrAdd(
                        key,
                        tuple =>
                            new Lazy<Task<TestFhirServer>>(async () =>
                            {
                                TestFhirServer testFhirServer;
                                string environmentUrl = GetEnvironmentUrl(tuple.dataStore);

                                if (string.IsNullOrEmpty(environmentUrl))
                                {
                                    testFhirServer = new InProcTestFhirServer(tuple.dataStore, tuple.startupType);
                                }
                                else
                                {
                                    if (environmentUrl.Last() != '/')
                                    {
                                        environmentUrl = $"{environmentUrl}/";
                                    }

                                    testFhirServer = new RemoteTestFhirServer(environmentUrl);
                                }

                                await testFhirServer.ConfigureSecurityOptions();

                                // Perform auth warmup to fail fast if authentication is not working
                                await WarmupAuthenticationAsync(testFhirServer, tuple.dataStore);

                                return testFhirServer;
                            }))
                    .Value;
            }
            catch (Exception ex)
            {
                // Track the failure so subsequent retry attempts can clear the cache
                Console.WriteLine($"[TestFhirServerFactory] Initialization failed for {dataStore}/{startupType?.Name}: {ex.Message}");
                _failedInitializations.TryAdd(key, ex);

                // Remove the failed Lazy from cache to ensure fresh attempt on retry
                _cache.TryRemove(key, out _);

                throw;
            }
        }

        /// <summary>
        /// Performs an authenticated request to verify that authentication is working.
        /// If authentication fails after all retries (handled by RetryAuthenticationHttpMessageHandler),
        /// this method throws an exception to fail the entire test assembly fast.
        /// </summary>
        private static async Task WarmupAuthenticationAsync(TestFhirServer testFhirServer, DataStore dataStore)
        {
            if (!testFhirServer.SecurityEnabled)
            {
                Console.WriteLine("[TestFhirServerFactory] Security is not enabled, skipping auth warmup.");
                return;
            }

            Console.WriteLine($"[TestFhirServerFactory] Performing auth warmup for {dataStore} server at {testFhirServer.BaseAddress}...");

            try
            {
                // Create a test client with authentication - this will use the retry handler
                var testClient = testFhirServer.GetTestFhirClient(ResourceFormat.Json);

                // Make a simple authenticated request - the Patient search with _count=0 is lightweight
                using var request = new HttpRequestMessage(HttpMethod.Get, "Patient?_count=0");
                using var response = await testClient.HttpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // If we still get 401 after all retries, fail fast with a clear message
                    string responseBody = await response.Content.ReadAsStringAsync();
                    throw new AuthenticationWarmupException(
                        $"Authentication warmup failed for {dataStore} server at {testFhirServer.BaseAddress}. " +
                        $"The server returned 401 Unauthorized after all retry attempts. " +
                        $"This indicates a persistent authentication issue. " +
                        $"Response: {responseBody}");
                }

                // Any other response (200, 404, etc.) means auth is working
                Console.WriteLine($"[TestFhirServerFactory] Auth warmup successful for {dataStore}. Response status: {response.StatusCode}");
            }
            catch (AuthenticationWarmupException)
            {
                // Re-throw auth warmup exceptions as-is
                throw;
            }
            catch (Exception ex)
            {
                // Wrap other exceptions with context
                throw new AuthenticationWarmupException(
                    $"Authentication warmup failed for {dataStore} server at {testFhirServer.BaseAddress}. " +
                    $"Exception: {ex.Message}",
                    ex);
            }
        }

        private static string GetEnvironmentUrl(DataStore dataStore)
        {
            switch (dataStore)
            {
                case DataStore.CosmosDb:
                    return EnvironmentVariables.GetEnvironmentVariable($"{KnownEnvironmentVariableNames.TestEnvironmentUrl}{Constants.TestEnvironmentVariableVersionSuffix}");
                case DataStore.SqlServer:
                    return EnvironmentVariables.GetEnvironmentVariable($"{KnownEnvironmentVariableNames.TestEnvironmentUrl}{Constants.TestEnvironmentVariableVersionSuffix}_Sql");
                default:
                    throw new ArgumentOutOfRangeException(nameof(dataStore), dataStore, null);
            }
        }

        Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

        async Task IAsyncLifetime.DisposeAsync()
        {
            await DisposeAsync();
        }

        public async ValueTask DisposeAsync()
        {
            foreach (Lazy<Task<TestFhirServer>> cacheValue in _cache.Values)
            {
                if (cacheValue.IsValueCreated)
                {
                    await (await cacheValue.Value).DisposeAsync();
                }
            }
        }
    }
}
