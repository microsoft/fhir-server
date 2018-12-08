// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.SmartProxy
{
    public class SmartProxyTestFixture : IDisposable
    {
        private string _environmentUrl;

        public SmartProxyTestFixture()
        {
            _environmentUrl = Environment.GetEnvironmentVariable("TestEnvironmentUrl");
            SmartLauncherUrl = "https://localhost:" + Port.ToString();

            Environment.SetEnvironmentVariable("FhirServerUrl", _environmentUrl);
            Environment.SetEnvironmentVariable("ClientId", TestApplications.NativeClient.ClientId);

            var builder = WebHost.CreateDefaultBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json");
                    config.AddEnvironmentVariables();
                })
                .UseStartup<SmartLauncher.Startup>()
                .UseUrls(SmartLauncherUrl)
                .UseContentRoot(Path.Combine(Directory.GetCurrentDirectory(), "../../../../../samples/apps/SmartLauncher"));

            WebServer = builder.Build();
            WebServer.Start();

            // _server = new TestServer(builder);

            HttpClient = new HttpClient() { BaseAddress = new Uri(_environmentUrl) };

            FhirClient = new FhirClient(HttpClient, ResourceFormat.Json);
        }

        public IWebHost WebServer { get; private set; }

        public int Port { get; } = 6001;

        public string SmartLauncherUrl { get; private set; }

        public HttpClient HttpClient { get; }

        public FhirClient FhirClient { get; }

        public void Dispose()
        {
            HttpClient.Dispose();
            WebServer?.Dispose();
        }
    }
}