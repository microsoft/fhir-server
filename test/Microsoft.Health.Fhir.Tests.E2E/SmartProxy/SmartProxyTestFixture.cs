// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Tests.E2E.SmartProxy
{
    public class SmartProxyTestFixture : IDisposable
    {
        public SmartProxyTestFixture()
        {
            var builder = WebHost.CreateDefaultBuilder()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json");
                    config.AddEnvironmentVariables();
                    config.AddUserSecrets<SmartLauncher.Startup>();
                })
                .UseStartup<SmartLauncher.Startup>()
                .UseUrls("https://localhost:" + Port.ToString())
                .UseContentRoot(Path.Combine(Directory.GetCurrentDirectory(), "../../../../../samples/apps/SmartLauncher"));

            WebServer = builder.Build();
            WebServer.Start();
        }

        public IWebHost WebServer { get; private set; }

        public int Port { get; } = 6001;

        public void Dispose()
        {
            WebServer?.StopAsync(new TimeSpan(1000));
            WebServer.WaitForShutdown();
        }
    }
}