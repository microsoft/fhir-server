// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.SmartProxy
{
    public class SmartProxyTestFixture : IDisposable
    {
        public SmartProxyTestFixture()
        {
            string environmentUrl = Environment.GetEnvironmentVariable("TestEnvironmentUrl");

            // Only set up test fixture if running against remote server
            if (!string.IsNullOrWhiteSpace(environmentUrl))
            {
                var baseUrl = "https://localhost:" + Port;
                SmartLauncherUrl = baseUrl + "/index.html";

                Environment.SetEnvironmentVariable("FhirServerUrl", environmentUrl);
                Environment.SetEnvironmentVariable("ClientId", TestApplications.NativeClient.ClientId);
                Environment.SetEnvironmentVariable("DefaultSmartAppUrl", "/sampleapp/launch.html");

                var builder = WebHost.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((hostingContext, config) =>
                    {
                        config.AddEnvironmentVariables();
                    })
                    .UseStartup<SmartLauncher.Startup>()
                    .UseUrls(baseUrl);

                WebServer = builder.Build();
                WebServer.Start();

                HttpClient = new HttpClient { BaseAddress = new Uri(environmentUrl), };

                FhirClient = new FhirClient(HttpClient, ResourceFormat.Json);
            }
        }

        public IWebHost WebServer { get; }

        public int Port { get; } = 6001;

        public string SmartLauncherUrl { get; }

        public HttpClient HttpClient { get; }

        public FhirClient FhirClient { get; }

        public void Dispose()
        {
            HttpClient?.Dispose();
            WebServer?.Dispose();
        }
    }
}
