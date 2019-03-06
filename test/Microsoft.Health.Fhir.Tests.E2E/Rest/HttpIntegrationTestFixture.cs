// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Web;
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
        private string _environmentUrl;
        private HttpMessageHandler _messageHandler;

        public HttpIntegrationTestFixture()
            : this(Path.Combine("src"))
        {
        }

        protected HttpIntegrationTestFixture(string targetProjectParentDirectory)
        {
            SetUpEnvironmentVariables();

            string environmentUrl = Environment.GetEnvironmentVariable("TestEnvironmentUrl");

            if (string.IsNullOrWhiteSpace(environmentUrl))
            {
                environmentUrl = "http://localhost/";

                StartInMemoryServer(targetProjectParentDirectory);

                _messageHandler = Server.CreateHandler();
                IsUsingInProcTestServer = true;
            }
            else
            {
                if (environmentUrl.Last() != '/')
                {
                    environmentUrl = $"{environmentUrl}/";
                }

                _messageHandler = new HttpClientHandler();
            }

            _environmentUrl = environmentUrl;

            HttpClient = CreateHttpClient();

            FhirClient = new FhirClient(HttpClient, ResourceFormat.Json);
            FhirXmlClient = new Lazy<FhirClient>(() => new FhirClient(HttpClient, ResourceFormat.Xml));
        }

        public bool IsUsingInProcTestServer { get; }

        public HttpClient HttpClient { get; }

        public FhirClient FhirClient { get; }

        public Lazy<FhirClient> FhirXmlClient { get; set; }

        protected TestServer Server { get; private set; }

        public string GenerateFullUrl(string relativeUrl)
        {
            return $"{_environmentUrl}{relativeUrl}";
        }

        public HttpClient CreateHttpClient()
            => new HttpClient(new SessionMessageHandler(_messageHandler)) { BaseAddress = new Uri(_environmentUrl) };

        private void StartInMemoryServer(string targetProjectParentDirectory)
        {
            var contentRoot = GetProjectPath(targetProjectParentDirectory, typeof(TStartup));
            var corsPath = Path.GetFullPath("corstestconfiguration.json");

            var builder = WebHost.CreateDefaultBuilder()
                .UseContentRoot(contentRoot)
                .ConfigureAppConfiguration(configurationBuilder =>
                {
                    configurationBuilder.AddDevelopmentAuthEnvironment("testauthenvironment.json");
                    configurationBuilder.AddJsonFile(corsPath);
                })
                .UseStartup(typeof(TStartup))
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
            Server?.Dispose();
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

            throw new Exception($"Project root could not be located for startup type {startupType.FullName}");
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
