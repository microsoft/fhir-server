// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// A test fixture that is intended to provide a <see cref="TestFhirClient"/> to end-to-end FHIR test classes.
    /// </summary>
    /// <typeparam name="TStartup">The type to use as the ASP.NET startup type when hosting the fhir server in-process</typeparam>
    public class HttpIntegrationTestFixture<TStartup> : IAsyncLifetime
    {
        private const int MaxInitializationRetries = 3;
        private static readonly TimeSpan InitializationRetryDelay = TimeSpan.FromSeconds(30);

        private readonly DataStore _dataStore;
        private readonly TestFhirServerFactory _testFhirServerFactory;
        private readonly ResourceFormat _resourceFormat;

        public HttpIntegrationTestFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
        {
            EnsureArg.IsNotNull(testFhirServerFactory, nameof(testFhirServerFactory));
            _dataStore = dataStore;
            _testFhirServerFactory = testFhirServerFactory;

            _resourceFormat = format switch
            {
                Format.Json => ResourceFormat.Json,
                Format.Xml => ResourceFormat.Xml,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
            };
        }

        public DataStore DataStore => _dataStore;

        public bool IsUsingInProcTestServer { get; private set; }

        /// <summary>
        /// Gets the exception that occurred during initialization, if any.
        /// This allows tests to see why the fixture failed rather than just getting NullReferenceException.
        /// </summary>
        public Exception InitializationException { get; private set; }

        public HttpClient HttpClient => TestFhirClient?.HttpClient;

        public TestFhirClient TestFhirClient { get; private set; }

        protected internal TestFhirServer TestFhirServer { get; private set; }

        public async Task InitializeAsync()
        {
            Exception lastException = null;

            for (int attempt = 1; attempt <= MaxInitializationRetries; attempt++)
            {
                try
                {
                    Console.WriteLine($"[HttpIntegrationTestFixture] Initialization attempt {attempt}/{MaxInitializationRetries} for {_dataStore}...");

                    TestFhirServer = await _testFhirServerFactory.GetTestFhirServerAsync(_dataStore, typeof(TStartup));
                    TestFhirClient = TestFhirServer.GetTestFhirClient(_resourceFormat);
                    IsUsingInProcTestServer = TestFhirServer is InProcTestFhirServer;

                    await OnInitializedAsync();

                    Console.WriteLine($"[HttpIntegrationTestFixture] Initialization successful for {_dataStore} on attempt {attempt}.");
                    return; // Success!
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    Console.WriteLine($"[HttpIntegrationTestFixture] Initialization attempt {attempt}/{MaxInitializationRetries} failed for {_dataStore}: {ex.Message}");

                    if (attempt < MaxInitializationRetries)
                    {
                        Console.WriteLine($"[HttpIntegrationTestFixture] Waiting {InitializationRetryDelay.TotalSeconds}s before retry...");
                        await Task.Delay(InitializationRetryDelay);
                    }
                }
            }

            // All retries exhausted - store the exception and log failure
            InitializationException = lastException;
            Console.WriteLine($"[HttpIntegrationTestFixture] CRITICAL: All {MaxInitializationRetries} initialization attempts failed for {_dataStore}. Tests using this fixture will fail.");
            Console.WriteLine($"[HttpIntegrationTestFixture] Last exception: {lastException}");

            // Re-throw to mark fixture as failed - but tests will still run with null client
            // At least now InitializationException is set for better error messages
            throw new InvalidOperationException(
                $"Test fixture initialization failed after {MaxInitializationRetries} attempts for {_dataStore}. " +
                $"Last error: {lastException?.Message}",
                lastException);
        }

        public async Task DisposeAsync()
        {
            await OnDisposedAsync();
        }

        public string GenerateFullUrl(string relativeUrl)
        {
            return $"{TestFhirServer.BaseAddress}{relativeUrl}";
        }

        protected virtual Task OnInitializedAsync() => Task.CompletedTask;

        protected virtual Task OnDisposedAsync() => Task.CompletedTask;
    }
}
