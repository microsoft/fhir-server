// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Creates and caches <see cref="TestFhirServer"/> instances. This class is intended to be used as an assembly fixture,
    /// so that the <see cref="TestFhirServer"/> instances can be reused across test classes in an assembly.
    /// </summary>
    public class TestFhirServerFactory : IAsyncLifetime, IAsyncDisposable
    {
        private readonly ConcurrentDictionary<(DataStore dataStore, Type startupType), Lazy<Task<TestFhirServer>>> _cache = new ConcurrentDictionary<(DataStore dataStore, Type startupType), Lazy<Task<TestFhirServer>>>();

        public async Task<TestFhirServer> GetTestFhirServerAsync(DataStore dataStore, Type startupType)
        {
            return await _cache.GetOrAdd(
                    (dataStore, startupType),
                    tuple =>
                        new Lazy<Task<TestFhirServer>>(() =>
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

                            return testFhirServer.ConfigureSecurityOptions().ContinueWith(_ => testFhirServer, TaskContinuationOptions.ExecuteSynchronously);
                        }))
                .Value;
        }

        private static string GetEnvironmentUrl(DataStore dataStore)
        {
            switch (dataStore)
            {
                case DataStore.CosmosDb:
                    return Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSuffix}");
                case DataStore.SqlServer:
                    return Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSuffix}_Sql");
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
