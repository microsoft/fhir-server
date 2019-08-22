// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Web;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// A <see cref="TestFhirServer"/> that runs the FHIR server in-process and creates
    /// using asp.net's <see cref="TestServer"/>.
    /// </summary>
    public class InProcTestFhirServer : TestFhirServer
    {
        private readonly HttpMessageHandler _messageHandler;

        public InProcTestFhirServer(DataStore dataStore, Type startupType)
            : base(new Uri("http://localhost/"))
        {
            var contentRoot = GetProjectPath("src", startupType);
            var corsPath = Path.GetFullPath("corstestconfiguration.json");
            var exportPath = Path.GetFullPath("exporttestconfiguration.json");

            var launchSettings = JObject.Parse(File.ReadAllText(Path.Combine(contentRoot, "Properties", "launchSettings.json")));

            var configuration = launchSettings["profiles"][dataStore.ToString()]["environmentVariables"].Cast<JProperty>().ToDictionary(p => p.Name, p => p.Value.ToString());

            var builder = WebHost.CreateDefaultBuilder()
                .UseContentRoot(contentRoot)
                .ConfigureAppConfiguration(configurationBuilder =>
                {
                    configurationBuilder.AddDevelopmentAuthEnvironment("testauthenvironment.json");
                    configurationBuilder.AddJsonFile(corsPath);
                    configurationBuilder.AddJsonFile(exportPath);
                    configurationBuilder.AddInMemoryCollection(configuration);
                })
                .UseStartup(startupType)
                .ConfigureServices(serviceCollection =>
                {
                    // ensure that HttpClients
                    // use a message handler for the test server
                    serviceCollection
                        .AddHttpClient(Options.DefaultName)
                        .ConfigurePrimaryHttpMessageHandler(() => _messageHandler);

                    serviceCollection.PostConfigure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options => options.BackchannelHttpHandler = _messageHandler);
                });

            Server = new TestServer(builder);
            _messageHandler = new SuppressExecutionContextHandler(Server.CreateHandler());
        }

        public TestServer Server { get; }

        protected override HttpMessageHandler CreateMessageHandler()
        {
            return _messageHandler;
        }

        public override void Dispose()
        {
            Server?.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// Gets the full path to the target project that we wish to test
        /// </summary>
        /// <param name="projectRelativePath">
        /// The parent directory of the target project.
        /// e.g. src, samples, test, or test/Websites
        /// </param>
        /// <param name="startupType">The startup type</param>
        /// <returns>The full path to the target project.</returns>
        private static string GetProjectPath(string projectRelativePath, Type startupType)
        {
            for (Type type = startupType; type != null; type = type.BaseType)
            {
                // Get name of the target project which we want to test
                var projectName = type.GetTypeInfo().Assembly.GetName().Name;

                // Get currently executing test project path
                var applicationBasePath = System.AppContext.BaseDirectory;

                // Find the path to the target project
                var directoryInfo = new DirectoryInfo(applicationBasePath);
                do
                {
                    directoryInfo = directoryInfo.Parent;

                    var projectDirectoryInfo = new DirectoryInfo(Path.Combine(directoryInfo.FullName, projectRelativePath));
                    if (projectDirectoryInfo.Exists)
                    {
                        var projectFileInfo = new FileInfo(Path.Combine(projectDirectoryInfo.FullName, projectName, $"{projectName}.csproj"));
                        if (projectFileInfo.Exists)
                        {
                            return Path.Combine(projectDirectoryInfo.FullName, projectName);
                        }
                    }
                }
                while (directoryInfo.Parent != null);
            }

            throw new InvalidOperationException($"Project root could not be located for startup type {startupType.FullName}");
        }
    }
}
