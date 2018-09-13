﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// A test fixture which hosts the target web project in an in-memory server.
    /// Code adapted from https://docs.microsoft.com/en-us/aspnet/core/mvc/controllers/testing#integration-testing
    /// </summary>
    /// <typeparam name="TStartup">The target web project startup</typeparam>
    public class HttpIntegrationTestFixture<TStartup> : IDisposable
    {
        private TestServer _server;
        private string _environmentUrl;

        public HttpIntegrationTestFixture()
            : this(Path.Combine("src"))
        {
        }

        protected HttpIntegrationTestFixture(string targetProjectParentDirectory)
        {
            SetUpEnvironmentVariables();

            _environmentUrl = Environment.GetEnvironmentVariable("TestEnvironmentUrl");

            HttpMessageHandler messageHandler;

            if (string.IsNullOrWhiteSpace(_environmentUrl))
            {
                _environmentUrl = "http://localhost/";

                StartInMemoryServer(targetProjectParentDirectory);

                messageHandler = _server.CreateHandler();
            }
            else
            {
                messageHandler = new HttpClientHandler();
            }

            HttpClient = new HttpClient(new SessionMessageHandler(messageHandler)) { BaseAddress = new Uri(_environmentUrl) };

            FhirClient = new FhirClient(HttpClient, ResourceFormat.Json);
            FhirXmlClient = new Lazy<FhirClient>(() => new FhirClient(HttpClient, ResourceFormat.Xml));
        }

        public HttpClient HttpClient { get; }

        public FhirClient FhirClient { get; }

        public Lazy<FhirClient> FhirXmlClient { get; set; }

        private void StartInMemoryServer(string targetProjectParentDirectory)
        {
            var startupAssembly = typeof(TStartup).GetTypeInfo().Assembly;
            var contentRoot = GetProjectPath(targetProjectParentDirectory, startupAssembly);

            var builder = WebHost.CreateDefaultBuilder()
                .UseContentRoot(contentRoot)
                .ConfigureAppConfiguration((hostContext, configBuilder) =>
                {
                    var securityConfig = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("Security:Enabled", "false"),
                    };

                    configBuilder.AddInMemoryCollection(securityConfig);
                })
                .UseStartup(typeof(TStartup));

            _server = new TestServer(builder);
        }

        /// <summary>
        /// Method to set up environment variables based on the project's defined environment variables.
        /// These are used to target the http integration tests to a server that isn't hosted in memory.
        /// For this method to function, the launchSettings.json file must be copied to the output directory of the project.
        /// </summary>
        private static void SetUpEnvironmentVariables()
        {
            var settingsPath = @"Properties\launchSettings.json";
            if (File.Exists(settingsPath))
            {
                using (var file = File.OpenText(settingsPath))
                {
                    using (var jsonReader = new JsonTextReader(file))
                    {
                        var launchSettings = JObject.Load(jsonReader);
                        var environmentVariables = (JObject)launchSettings.SelectToken("$.profiles.['Microsoft.Health.Fhir.Tests.Integration'].environmentVariables");

                        if (environmentVariables != null)
                        {
                            foreach (var environmentVariable in environmentVariables)
                            {
                                Environment.SetEnvironmentVariable(environmentVariable.Key, environmentVariable.Value.ToString());
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            HttpClient.Dispose();
            _server?.Dispose();
        }

        protected virtual void InitializeServices(IServiceCollection services)
        {
            var startupAssembly = typeof(TStartup).GetTypeInfo().Assembly;

            // Inject a custom application part manager.
            // Overrides AddMvcCore() because it uses TryAdd().
            var manager = new ApplicationPartManager();
            manager.ApplicationParts.Add(new AssemblyPart(startupAssembly));
            manager.FeatureProviders.Add(new ControllerFeatureProvider());
            manager.FeatureProviders.Add(new ViewComponentFeatureProvider());

            services.AddSingleton(manager);
        }

        /// <summary>
        /// Gets the full path to the target project that we wish to test
        /// </summary>
        /// <param name="projectRelativePath">
        /// The parent directory of the target project.
        /// e.g. src, samples, test, or test/Websites
        /// </param>
        /// <param name="startupAssembly">The target project's assembly.</param>
        /// <returns>The full path to the target project.</returns>
        private static string GetProjectPath(string projectRelativePath, Assembly startupAssembly)
        {
            // Get name of the target project which we want to test
            var projectName = startupAssembly.GetName().Name;

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

            throw new Exception($"Project root could not be located using the application root {applicationBasePath}.");
        }

        /// <summary>
        /// An <see cref="HttpMessageHandler"/> that maintains session consistency between requests.
        /// </summary>
        private class SessionMessageHandler : DelegatingHandler
        {
            private string _sessionToken;

            public SessionMessageHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (!string.IsNullOrEmpty(_sessionToken))
                {
                    request.Headers.TryAddWithoutValidation("x-ms-session-token", _sessionToken);
                }

                request.Headers.TryAddWithoutValidation("x-ms-consistency-level", "Session");

                var response = await base.SendAsync(request, cancellationToken);

                if (response.Headers.TryGetValues("x-ms-session-token", out var tokens))
                {
                    _sessionToken = tokens.SingleOrDefault();
                }

                return response;
            }
        }
    }
}
