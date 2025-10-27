// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Internal.SmartLauncher;
using Xunit;

namespace Microsoft.Health.Fhir.Smart.Tests.E2E.SmartProxy
{
    public class SmartProxyTestFixture : IDisposable, IAsyncLifetime
    {
        private readonly TestFhirServerFactory _testFhirServerFactory;
        private IHost _host;

        public SmartProxyTestFixture(TestFhirServerFactory testFhirServerFactory)
        {
            EnsureArg.IsNotNull(testFhirServerFactory, nameof(testFhirServerFactory));
            _testFhirServerFactory = testFhirServerFactory;
        }

        public IWebHost WebServer => _host?.Services.GetService(typeof(IWebHost)) as IWebHost;

        public int Port { get; private set; } = 6001;

        public string SmartLauncherUrl { get; private set; }

        public TestFhirClient TestFhirClient { get; private set; }

        public async Task InitializeAsync()
        {
            string environmentUrl = Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSuffix}");

            // Only set up test fixture if running against remote server
            if (!string.IsNullOrWhiteSpace(environmentUrl))
            {
                var baseUrl = "https://localhost:" + Port;
                SmartLauncherUrl = baseUrl + "/index.html";

                var hostBuilder = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((hostingContext, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string>
                        {
                            { "FhirServerUrl", environmentUrl },
                            { "ClientId", TestApplications.NativeClient.ClientId },
                            { "DefaultSmartAppUrl", "/sampleapp/launch.html" },
                        });
                    })
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder
                            .UseStartup<Startup>()
                            .UseUrls(baseUrl);
                    });

                _host = hostBuilder.Build();
                await _host.StartAsync();

                TestFhirServer testFhirServer = await _testFhirServerFactory.GetTestFhirServerAsync(DataStore.CosmosDb, null);
                TestFhirClient = testFhirServer.GetTestFhirClient(ResourceFormat.Json);
            }
        }

        public async Task DisposeAsync()
        {
            if (_host != null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }
        }

        public void Dispose()
        {
            _host?.Dispose();
        }
    }
}
