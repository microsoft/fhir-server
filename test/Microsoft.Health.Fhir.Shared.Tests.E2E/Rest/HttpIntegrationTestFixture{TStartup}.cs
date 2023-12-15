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

        public bool IsUsingInProcTestServer { get; private set; }

        public HttpClient HttpClient => TestFhirClient.HttpClient;

        public TestFhirClient TestFhirClient { get; private set; }

        protected internal TestFhirServer TestFhirServer { get; private set; }

        public async Task InitializeAsync()
        {
            TestFhirServer = await _testFhirServerFactory.GetTestFhirServerAsync(_dataStore, typeof(TStartup));

            TestFhirClient = TestFhirServer.GetTestFhirClient(_resourceFormat);

            IsUsingInProcTestServer = TestFhirServer is InProcTestFhirServer;

            await OnInitializedAsync();
        }

        public async Task DisposeAsync()
        {
            await OnDisposedAsync();
        }

        public string GenerateFullUrl(string relativeUrl)
        {
            return $"{TestFhirServer.BaseAddress}{relativeUrl}";
        }

        public Uri GenerateUri(string uriString)
        {
            if (Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out Uri result))
            {
                return result;
            }

            return null;
        }

        protected virtual Task OnInitializedAsync() => Task.CompletedTask;

        protected virtual Task OnDisposedAsync() => Task.CompletedTask;
    }
}
