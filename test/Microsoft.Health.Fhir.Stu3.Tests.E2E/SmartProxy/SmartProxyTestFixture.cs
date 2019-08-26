// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.SmartProxy
{
    public class SmartProxyTestFixture : IDisposable
    {
        public SmartProxyTestFixture(TestFhirServerFactory testFhirServerFactory)
        {
            EnsureArg.IsNotNull(testFhirServerFactory, nameof(testFhirServerFactory));

            string environmentUrl = Environment.GetEnvironmentVariable($"TestEnvironmentUrl{Constants.TestEnvironmentVariableVersionSuffix}");

            // Only set up test fixture if running against remote server
            if (!string.IsNullOrWhiteSpace(environmentUrl))
            {
                var baseUrl = "https://localhost:" + Port;
                SmartLauncherUrl = baseUrl + "/index.html";

                var builder = WebHost.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((hostingContext, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string>
                        {
                            { "FhirServerUrl", environmentUrl },
                            { "ClientId", TestApplications.NativeClient.ClientId },
                            { "DefaultSmartAppUrl", "/sampleapp/launch.html" },
                        });
                    })
                    .UseStartup<SmartLauncher.Startup>()
                    .UseUrls(baseUrl);

                WebServer = builder.Build();
                WebServer.Start();

                FhirClient = testFhirServerFactory.GetTestFhirServer(DataStore.CosmosDb, null).GetFhirClient(ResourceFormat.Json);
            }
        }

        public IWebHost WebServer { get; }

        public int Port { get; } = 6001;

        public string SmartLauncherUrl { get; }

        public FhirClient FhirClient { get; }

        public void Dispose()
        {
            WebServer?.Dispose();
        }
    }
}
