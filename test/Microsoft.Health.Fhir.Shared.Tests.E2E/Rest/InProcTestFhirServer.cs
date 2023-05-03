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
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
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
        private readonly Func<Task> _cleanupDatabase;
        private readonly IConfiguration _builtConfiguration;

        public InProcTestFhirServer(DataStore dataStore, Type startupType)
            : base(new Uri("http://localhost/"))
        {
            var projectDir = GetProjectPath("src", startupType);
            var testConfigPath = Path.GetFullPath("testconfiguration.json");

            var launchSettings = JObject.Parse(File.ReadAllText(Path.Combine(projectDir, "Properties", "launchSettings.json")));

            var configuration = launchSettings["profiles"][dataStore.ToString()]["environmentVariables"].Cast<JProperty>().ToDictionary(p => p.Name, p => p.Value.ToString());

            configuration["TestAuthEnvironment:FilePath"] = "testauthenvironment.json";
            configuration["FhirServer:Security:Enabled"] = "true";

            string inProcEndpoint = "https://inprochost";
            configuration["FhirServer:Security:Authentication:Authority"] = inProcEndpoint;

            // For local development we will use the Azure Storage Emulator for export.
            configuration["FhirServer:Operations:Export:StorageAccountConnection"] = "UseDevelopmentStorage=true";

            // enable reindex for testing
            configuration["FhirServer:Operations:Reindex:Enabled"] = "true";

            // enable import for testing
            configuration["FhirServer:Operations:Import:Enabled"] = "true";
            configuration["FhirServer:Operations:Import:PollingFrequencyInSeconds"] = "2";
            configuration["FhirServer:Operations:IntegrationDataStore:StorageAccountConnection"] = "UseDevelopmentStorage=true";

            // enable rebuild indexes for testing
            configuration["FhirServer:Operations:Import:DisableOptionalIndexesForImport"] = "false";
            configuration["FhirServer:Operations:Import:DisableUniqueOptionalIndexesForImport"] = "false";

            if (startupType.IsDefined(typeof(RequiresIsolatedDatabaseAttribute)))
            {
                // Alter the configuration so that the server will create a new, isolated database/container.
                // Ensure tha the database/container is deleted at the end of the test run (when this instance is disposed)

                if (dataStore == DataStore.SqlServer)
                {
                    var connectionStringBuilder = new SqlConnectionStringBuilder(configuration["SqlServer:ConnectionString"]);
                    var databaseName = connectionStringBuilder.InitialCatalog += "_" + startupType.Name;
                    configuration["SqlServer:ConnectionString"] = connectionStringBuilder.ToString();
                    configuration["TaskHosting:Enabled"] = "true";
                    configuration["TaskHosting:MaxRunningTaskCount"] = "2";
                    configuration["TaskHosting:PollingFrequencyInSeconds"] = "2";

                    _cleanupDatabase = async () =>
                    {
                        connectionStringBuilder.InitialCatalog = "master";
                        await using var connection = new SqlConnection(connectionStringBuilder.ToString());

                        await connection.OpenAsync();
                        SqlConnection.ClearAllPools();

                        await using SqlCommand command = connection.CreateCommand();
                        command.CommandTimeout = 600;
                        command.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
                        await command.ExecuteNonQueryAsync();
                    };
                }
                else if (dataStore == DataStore.CosmosDb)
                {
                    var collectionId = configuration["FhirServer:CosmosDb:CollectionId"] = $"fhir{ModelInfoProvider.Version}_{startupType.Name}";

                    _cleanupDatabase = async () =>
                    {
                        string ValueOrFallback(string configKey, string fallbackValue)
                        {
                            string value = _builtConfiguration[configKey];
                            return string.IsNullOrEmpty(value) ? fallbackValue : value;
                        }

                        var host = ValueOrFallback("CosmosDb:Host", CosmosDbLocalEmulator.Host);
                        var key = ValueOrFallback("CosmosDb:Key", CosmosDbLocalEmulator.Key);
                        var databaseId = ValueOrFallback("CosmosDb:DatabaseId", null) ?? throw new InvalidOperationException("expected CosmosDb:DatabaseId to be set in configuration");

                        using var client = new CosmosClient(host, key);
                        Container container = client.GetContainer(databaseId, collectionId);
                        await container.DeleteContainerAsync();
                    };
                }
            }

            var builder = WebHost.CreateDefaultBuilder()
                .UseContentRoot(Path.GetDirectoryName(startupType.Assembly.Location))
                .ConfigureAppConfiguration(configurationBuilder =>
                {
                    configurationBuilder.AddDevelopmentAuthEnvironmentIfConfigured(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
                    configurationBuilder.AddJsonFile(testConfigPath);
                    configurationBuilder.AddInMemoryCollection(configuration);
                })
                .UseStartup(startupType)
                .ConfigureServices(serviceCollection =>
                {
                    // ensure that HttpClients
                    // use a message handler for the test server
                    serviceCollection
                        .AddHttpClient(Options.DefaultName)
                        .ConfigurePrimaryHttpMessageHandler(() => new DispatchingHandler(Server.CreateHandler(), inProcEndpoint))
                        .SetHandlerLifetime(Timeout.InfiniteTimeSpan); // So that it is not disposed after 2 minutes;

                    serviceCollection.PostConfigure<JwtBearerOptions>(
                        JwtBearerDefaults.AuthenticationScheme,
                        options => options.BackchannelHttpHandler = Server.CreateHandler());
                });

            Server = new TestServer(builder);

            _builtConfiguration = Server.Services.GetRequiredService<IConfiguration>();
        }

        public TestServer Server { get; }

        internal override HttpMessageHandler CreateMessageHandler()
        {
            return Server.CreateHandler();
        }

        public override async ValueTask DisposeAsync()
        {
            Server?.Dispose();

            if (_cleanupDatabase != null)
            {
                await _cleanupDatabase();
            }

            await base.DisposeAsync();
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

        private class DispatchingHandler : DelegatingHandler
        {
            private readonly HttpMessageHandler _testServerHandler;
            private readonly string _inProcEndpointPrefix;
            private readonly ExternalHandler _externalHandler;

            public DispatchingHandler(HttpMessageHandler testServerHandler, string inProcEndpointPrefix)
                : base(testServerHandler)
            {
                _testServerHandler = testServerHandler;
                _inProcEndpointPrefix = inProcEndpointPrefix;
                _externalHandler = new ExternalHandler();
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                if (request.RequestUri.ToString().StartsWith(_inProcEndpointPrefix))
                {
                    return await base.SendAsync(request, cancellationToken);
                }

                return await _externalHandler.SendPublicAsync(request, cancellationToken);
            }

            public class ExternalHandler : HttpClientHandler
            {
                public async Task<HttpResponseMessage> SendPublicAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                {
                    return await SendAsync(request, cancellationToken);
                }
            }
        }
    }
}
