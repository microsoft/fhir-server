// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Creates and caches <see cref="TestFhirServer"/> instances. This class is intended to be used as an assembly fixture,
    /// so that the <see cref="TestFhirServer"/> instances can be reused across test classes in an assembly.
    /// </summary>
    public class TestFhirServerFactory : IDisposable
    {
        private readonly ConcurrentDictionary<(DataStore dataStore, Type startupType), Lazy<TestFhirServer>> _cache = new ConcurrentDictionary<(DataStore dataStore, Type startupType), Lazy<TestFhirServer>>();

        public TestFhirServer GetTestFhirServer(DataStore dataStore, Type startupType)
        {
            return _cache.GetOrAdd(
                    (dataStore, startupType),
                    tuple =>
                        new Lazy<TestFhirServer>(() =>
                        {
                            string environmentUrl = GetEnvironmentUrl(tuple.dataStore);

                            if (string.IsNullOrEmpty(environmentUrl))
                            {
                                return new InProcTestFhirServer(tuple.dataStore, tuple.startupType);
                            }

                            if (environmentUrl.Last() != '/')
                            {
                                environmentUrl = $"{environmentUrl}/";
                            }

                            return new RemoteTestFhirServer(environmentUrl);
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

        public void Dispose()
        {
            foreach (Lazy<TestFhirServer> cacheValue in _cache.Values)
            {
                if (cacheValue.IsValueCreated)
                {
                    cacheValue.Value.Dispose();
                }
            }
        }
    }
}
