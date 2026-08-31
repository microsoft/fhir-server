// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.ApplicationInsights.AspNetCore.Extensions;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
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
            Startup startup = new Startup(configuration);
            var addTelemetryProviderMethod = startup.GetType().GetMethod(AddTelemetryProviderMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addTelemetryProviderMethod.Invoke(startup, new object[] { services });

            using var provider = services.BuildServiceProvider();
            if (!string.IsNullOrWhiteSpace(instrumentationKey) || !string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Contains(services, descriptor => descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("ApplicationInsights", StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(services, descriptor =>
                    descriptor.ImplementationType != null && descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase));

                var configureServiceOptions = provider.GetRequiredService<IConfigureOptions<ApplicationInsightsServiceOptions>>();
                var serviceOptions = new ApplicationInsightsServiceOptions();
                configureServiceOptions?.Configure(serviceOptions);
#pragma warning disable CS0618 // Type or member is obsolete
                Assert.True(
                    !string.IsNullOrWhiteSpace(instrumentationKey)
                        ? serviceOptions.InstrumentationKey == instrumentationKey && serviceOptions.ConnectionString == null
                        : serviceOptions.ConnectionString == connectionString && serviceOptions.InstrumentationKey == null);
#pragma warning restore CS0618 // Type or member is obsolete

                var configureTelemetryConfiguration = provider.GetRequiredService<IConfigureOptions<TelemetryConfiguration>>();
                using var telemetryConfiguration = new TelemetryConfiguration();
                configureTelemetryConfiguration?.Configure(telemetryConfiguration);
#pragma warning disable CS0618 // Type or member is obsolete
                Assert.True(
                    !string.IsNullOrWhiteSpace(instrumentationKey)
                        ? telemetryConfiguration.InstrumentationKey == instrumentationKey && string.IsNullOrEmpty(telemetryConfiguration.ConnectionString)
                        : telemetryConfiguration.ConnectionString == connectionString);
#pragma warning restore CS0618 // Type or member is obsolete
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
    }
}
