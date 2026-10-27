// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Web.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class StartupTests
    {
        private const string DefaultInstrumentationKey = "11111111-2222-3333-4444-555555555555";
        private const string DefaultConnectionString = "InstrumentationKey=11111111-2222-3333-4444-555555555555;IngestionEndpoint=https://local.applicationinsights.azure.com/;LiveEndpoint=https://local.livediagnostics.monitor.azure.com/";

        private const string TelemetryConfigurationSectionName = "Telemetry";
        private const string TelemetryInstrumentationKeyConfigurationKey = "Telemetry:InstrumentationKey";
        private const string TelemetryConnectionStringConfigurationKey = "Telemetry:ConnectionString";
        private const string TelemetryProviderConfigurationKey = "Telemetry:Provider";
        private const string TelemetryProviderApplicationInsightsConfigurationValue = "ApplicationInsights";
        private const string TelemetryProviderOpenTelemetryConfigurationValue = "OpenTelemetry";
        private const string TelemetryProviderNoneConfigurationValue = "None";
        private const string AddTelemetryProviderMethodName = "AddTelemetryProvider";
        private const string AddRuntimeConfigurationMethodName = "AddRuntimeConfiguration";
        private const string RuntimeStateConfigurationKey = "FhirServer:CoreFeatures:RuntimeState";

        [Theory]
        [InlineData(null, FhirRuntimeState.Active)]
        [InlineData("", FhirRuntimeState.Active)]
        [InlineData("Active", FhirRuntimeState.Active)]
        [InlineData("Deprecated", FhirRuntimeState.Deprecated)]
        public void GivenGen1RuntimeState_WhenAddingRuntimeConfiguration_ThenEffectiveStateIsRegistered(
            string configuredRuntimeState,
            FhirRuntimeState expectedRuntimeState)
        {
            // This test ensures that the default values are properly handled for Gen1 runtime state.
            // Empty/null values should be treated as "Active" for Gen1 runtime state.
            // Active/Deprecates values should be treated as-is for Gen1 runtime state.

            var settings = new Dictionary<string, string>
            {
                { "DataStore", KnownDataStores.CosmosDb },
                { RuntimeStateConfigurationKey, configuredRuntimeState },
            };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            var fhirServerBuilder = Substitute.For<IFhirServerBuilder>();
            fhirServerBuilder.Services.Returns(services);

            InvokeAddRuntimeConfiguration(configuration, fhirServerBuilder);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IFhirRuntimeConfiguration runtimeConfiguration = serviceProvider.GetRequiredService<IFhirRuntimeConfiguration>();
            Assert.Equal(expectedRuntimeState, runtimeConfiguration.RuntimeState);
        }

        [Fact]
        public void GivenDeprecatedGen2RuntimeState_WhenAddingRuntimeConfiguration_ThenEffectiveStateIsActive()
        {
            // This test ensures that the 'Active' is always handled for Gen2 runtime state, no matter what the configured value is.

            var settings = new Dictionary<string, string>
            {
                { "DataStore", KnownDataStores.SqlServer },
                { RuntimeStateConfigurationKey, FhirRuntimeState.Deprecated.ToString() },
            };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            var fhirServerBuilder = Substitute.For<IFhirServerBuilder>();
            fhirServerBuilder.Services.Returns(services);

            InvokeAddRuntimeConfiguration(configuration, fhirServerBuilder);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IFhirRuntimeConfiguration runtimeConfiguration = serviceProvider.GetRequiredService<IFhirRuntimeConfiguration>();
            Assert.Equal(FhirRuntimeState.Active, runtimeConfiguration.RuntimeState);
        }

        [Theory]
        [InlineData("Invalid")]
        [InlineData("1")]
        public void GivenInvalidGen1RuntimeState_WhenAddingRuntimeConfiguration_ThenInitializationContinuesAsActive(string configuredRuntimeState)
        {
            // This test ensures that invalid values are properly handled for Gen1 runtime state as 'Active'.

            var settings = new Dictionary<string, string>
            {
                { "DataStore", KnownDataStores.CosmosDb },
                { RuntimeStateConfigurationKey, configuredRuntimeState },
            };
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            var fhirServerBuilder = Substitute.For<IFhirServerBuilder>();
            fhirServerBuilder.Services.Returns(services);

            InvokeAddRuntimeConfiguration(configuration, fhirServerBuilder);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            IFhirRuntimeConfiguration runtimeConfiguration = serviceProvider.GetRequiredService<IFhirRuntimeConfiguration>();
            Assert.Equal(FhirRuntimeState.Active, runtimeConfiguration.RuntimeState);
        }

        [Fact]
        public void GivenAppSettings_WhenTelemetrySectionIsAbsent_ThenTelemetryProviderShouldBeDisabled()
        {
            IConfiguration configuration = Substitute.For<IConfiguration>();
            configuration[TelemetryConfigurationSectionName].ReturnsNull();

            IServiceCollection services = new ServiceCollection();
            Startup startup = new Startup(configuration);
            var addTelemetryProviderMethod = startup.GetType().GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addTelemetryProviderMethod.Invoke(startup, new object[] { services });

            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void GivenAppSettings_WhenTelemetryProviderIsNone_ThenTelemetryProviderShouldBeDisabled()
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderNoneConfigurationValue, null, null);
            IServiceCollection services = new ServiceCollection();
            Startup startup = new Startup(configuration);
            var addTelemetryProviderMethod = startup.GetType().GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addTelemetryProviderMethod.Invoke(startup, new object[] { services });

            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void GivenAppSettings_WhenConnectionInfoIsMissing_ThenTelemetryProviderShouldBeDisabled()
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderApplicationInsightsConfigurationValue, null, null);
            IServiceCollection services = new ServiceCollection();
            Startup startup = new Startup(configuration);
            var addTelemetryProviderMethod = startup.GetType().GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addTelemetryProviderMethod.Invoke(startup, new object[] { services });

            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [InlineData(DefaultInstrumentationKey, null)]
        [InlineData(null, DefaultConnectionString)]
        [InlineData(DefaultInstrumentationKey, DefaultConnectionString)]
        [InlineData(null, null)]
        public void GivenAppSettings_WhenTelemetryProviderIsApplicationInsignts_ThenApplicationInsigntsShouldBeEnabled(
            string instrumentationKey,
            string connectionString)
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderApplicationInsightsConfigurationValue, instrumentationKey, connectionString);
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton(configuration);
            Startup startup = new Startup(configuration);
            var addTelemetryProviderMethod = startup.GetType().GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addTelemetryProviderMethod.Invoke(startup, new object[] { services });

            using var provider = services.BuildServiceProvider();
            if (!string.IsNullOrWhiteSpace(instrumentationKey) || !string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Contains(services, descriptor => descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
                Assert.Contains(services, descriptor =>
                    descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));

                string expectedConnectionString = !string.IsNullOrWhiteSpace(instrumentationKey)
                    ? $"InstrumentationKey={instrumentationKey}"
                    : connectionString;
                var serviceOptions = provider.GetRequiredService<IOptions<ApplicationInsightsServiceOptions>>().Value;
                var telemetryConfiguration = provider.GetRequiredService<TelemetryConfiguration>();
                Assert.Equal(expectedConnectionString, serviceOptions.ConnectionString);
                Assert.Equal(expectedConnectionString, telemetryConfiguration.ConnectionString);
            }
            else
            {
                Assert.DoesNotContain(services, descriptor =>
                    descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(services, descriptor =>
                    descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Theory]
        [InlineData(null, null, null)]
        [InlineData("", null, "")]
        [InlineData("FHIR client/1.0", null, "FHIR client/1.0")]
        [InlineData("FHIR client/1.0", "another client/2.0", "FHIR client/1.0,another client/2.0")]
        public void GivenApplicationInsights_WhenEnrichingRequest_ThenUserAgentDimensionIsPreserved(
            string userAgent,
            string secondUserAgent,
            string expectedUserAgent)
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderApplicationInsightsConfigurationValue, null, DefaultConnectionString);
            var services = new ServiceCollection();
            var startup = new Startup(configuration);
            typeof(Startup).GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(startup, new object[] { services });
            using var provider = services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;
            var context = new DefaultHttpContext();
            if (userAgent != null)
            {
                context.Request.Headers.UserAgent = secondUserAgent == null
                    ? new StringValues(userAgent)
                    : new StringValues(new[] { userAgent, secondUserAgent });
            }

            using var activity = new Activity("request").Start();
            options.EnrichWithHttpRequest(activity, context.Request);

            Assert.Equal(expectedUserAgent, activity.GetTagItem("user_agent"));
        }

        [Fact]
        public void GivenApplicationInsights_WhenCreatingProviders_ThenCloudRoleIsPreservedAcrossSignals()
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderApplicationInsightsConfigurationValue, null, DefaultConnectionString);
            var services = new ServiceCollection();
            services.AddSingleton(configuration);
            var startup = new Startup(configuration);
            typeof(Startup).GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(startup, new object[] { services });
            using var provider = services.BuildServiceProvider();

            var traceResource = provider.GetRequiredService<TracerProvider>().GetResource();
            var metricResource = provider.GetRequiredService<MeterProvider>().GetResource();
            var logResource = provider.GetServices<ILoggerProvider>().OfType<OpenTelemetryLoggerProvider>().Single().GetResource();

            foreach (var resource in new[] { traceResource, metricResource, logResource })
            {
                Assert.Equal("Microsoft FHIR Server", resource.Attributes.Single(attribute => attribute.Key == "service.name").Value);
                Assert.Equal(string.Empty, resource.Attributes.Single(attribute => attribute.Key == "service.namespace").Value);
            }
        }

        [Fact]
        public async Task GivenApplicationInsights_WhenHandlingHttpRequest_ThenRequestTelemetryIncludesUserAgent()
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderApplicationInsightsConfigurationValue, null, DefaultConnectionString);
            var activities = new ConcurrentBag<Activity>();
            var logProperties = new ConcurrentDictionary<string, object>();
            using var transportClient = new HttpClient(new TelemetryHttpMessageHandler());
            using var host = new HostBuilder()
                .ConfigureWebHost(builder => builder
                    .UseKestrel()
                    .UseUrls("http://127.0.0.1:0")
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(configuration);
                        var startup = new Startup(configuration);
                        typeof(Startup).GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance)
                            .Invoke(startup, new object[] { services });
                        services.Configure<AzureMonitorExporterOptions>(options =>
                        {
                            options.Transport = new HttpClientTransport(transportClient);
                            options.EnableLiveMetrics = false;
                            options.DisableOfflineStorage = true;
                        });
                        services.ConfigureOpenTelemetryTracerProvider((_, tracerBuilder) =>
                            tracerBuilder.SetSampler(new AlwaysOnSampler()).AddProcessor(new RequestActivityProcessor(activities)));
                        services.ConfigureOpenTelemetryLoggerProvider((_, loggerBuilder) =>
                            loggerBuilder.AddProcessor(new LogScopeProcessor(logProperties)));
                    })
                    .Configure(app => app.Run(context =>
                    {
                        var logger = context.RequestServices.GetRequiredService<ILogger<Startup>>();
                        using var scope = logger.BeginScope(new Dictionary<string, object> { { "migration_scope", "preserved" } });
                        logger.LogWarning("Telemetry migration regression request.");
                        return context.Response.WriteAsync("OK");
                    })))
                .Build();
            await host.StartAsync();
            string address = host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.Single();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FHIR-client/1.0");

            using var response = await client.GetAsync(address);
            await host.StopAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var request = Assert.Single(activities, activity => activity.Kind == ActivityKind.Server);
            Assert.Equal("FHIR-client/1.0", request.GetTagItem("user_agent"));
            Assert.Equal("preserved", logProperties["migration_scope"]);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(DefaultConnectionString)]
        public void GivenAppSettings_WhenTelemetryProviderIsOpenTelemetry_ThenOpenTelemetryShouldBeEnabled(
            string connectionString)
        {
            IConfiguration configuration = BuildConfiguration(TelemetryProviderOpenTelemetryConfigurationValue, null, connectionString);
            IServiceCollection services = new ServiceCollection();
            Startup startup = new Startup(configuration);
            var addTelemetryProviderMethod = startup.GetType().GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addTelemetryProviderMethod.Invoke(startup, new object[] { services });

            using var provider = services.BuildServiceProvider();
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Contains(services, descriptor => descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                Assert.DoesNotContain(services, descriptor =>
                    descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));
            }

            Assert.DoesNotContain(services, descriptor =>
                descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
        }

        private IConfiguration BuildConfiguration(string provider, string instrumentationKey, string connectionString)
        {
            var telemetrySettings = new Dictionary<string, string>
            {
                { TelemetryProviderConfigurationKey, provider },
                { TelemetryInstrumentationKeyConfigurationKey, instrumentationKey },
                { TelemetryConnectionStringConfigurationKey, connectionString },
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(telemetrySettings)
                .Build();
            return configuration;
        }

        private static void InvokeAddRuntimeConfiguration(
            IConfiguration configuration,
            IFhirServerBuilder fhirServerBuilder)
        {
            MethodInfo addRuntimeConfigurationMethod = typeof(Startup).GetMethod(
                AddRuntimeConfigurationMethodName,
                BindingFlags.NonPublic | BindingFlags.Static);

            addRuntimeConfigurationMethod.Invoke(null, new object[] { configuration, fhirServerBuilder });
        }

        private sealed class RequestActivityProcessor(ConcurrentBag<Activity> activities) : BaseProcessor<Activity>
        {
            public override void OnEnd(Activity data)
            {
                activities.Add(data);
            }
        }

        private sealed class TelemetryHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        private sealed class LogScopeProcessor(ConcurrentDictionary<string, object> properties) : BaseProcessor<LogRecord>
        {
            public override void OnEnd(LogRecord data)
            {
                data.ForEachScope(
                    (scope, state) =>
                    {
                        foreach (var item in scope)
                        {
                            state[item.Key] = item.Value;
                        }
                    },
                    properties);
            }
        }
    }
}
