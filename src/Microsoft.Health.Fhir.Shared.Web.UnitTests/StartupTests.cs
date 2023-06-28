// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.ApplicationInsights;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.Test.Utilities;
using Moq;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Web.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class StartupTests
    {
        private const string ApplicationInsightsConfigurationName = "ApplicationInsights:InstrumentationKey";
        private const string AzureMonitorConfigurationName = "AzureMonitor:ConnectionString";
        private const string AddApplicationInsightsTelemetryMethodName = "AddApplicationInsightsTelemetry";
        private const string AddAzureMonitorOpenTelemetryMethodName = "AddAzureMonitorOpenTelemetry";

        [Fact]
        public void GivenAppSettings_WhenApplicationInsightsOnAndOpenTelemetryOff_ThenApplicationInsightsShouldBeEnabled()
        {
            string instrumentationKey = Guid.NewGuid().ToString();
            Mock<IConfiguration> configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[ApplicationInsightsConfigurationName]).Returns(instrumentationKey);
            configuration.Setup(x => x[AzureMonitorConfigurationName]).Returns((string)null);

            IList<ServiceDescriptor> serviceDescriptors = new List<ServiceDescriptor>();
            Mock<IServiceCollection> services = new Mock<IServiceCollection>();
            services.Setup(x => x.Count).Returns(serviceDescriptors.Count);
            services.Setup(x => x[It.IsAny<int>()]).Returns((int i) => serviceDescriptors[i]);
            services.Setup(x => x.Add(It.IsAny<ServiceDescriptor>())).Callback<ServiceDescriptor>((ServiceDescriptor descriptor) => serviceDescriptors.Add(descriptor));

            Startup startup = new Startup(configuration.Object);
            var addAppInsightsTelemetryMethod = startup.GetType().GetMethod(AddApplicationInsightsTelemetryMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addAppInsightsTelemetryMethod.Invoke(startup, new object[] { services.Object });
            Assert.Contains(serviceDescriptors, descriptor => descriptor.ImplementationType == typeof(ApplicationInsightsLoggerProvider));
            Assert.Contains(serviceDescriptors, descriptor => descriptor.ImplementationType == typeof(CloudRoleNameTelemetryInitializer));
            Assert.Contains(serviceDescriptors, descriptor => descriptor.ImplementationType == typeof(UserAgentHeaderTelemetryInitializer));

            serviceDescriptors.Clear();
            var addOpenTelemetryMethod = startup.GetType().GetMethod(AddAzureMonitorOpenTelemetryMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addOpenTelemetryMethod.Invoke(startup, new object[] { services.Object });
            Assert.Empty(serviceDescriptors);
        }

        [Fact]
        public void GivenAppSettings_WhenApplicationInsightsOnAndOpenTelemetryOn_ThenOpenTelemetryShouldBeEnabled()
        {
            string instrumentationKey = Guid.NewGuid().ToString();
            string connectionString = $"InstrumentationKey={Guid.NewGuid()};IngestionEndpoint=https://local.in.applicationinsights.azure.com/;LiveEndpoint=https://local.livediagnostics.monitor.azure.com/";
            Mock<IConfiguration> configuration = new Mock<IConfiguration>();
            configuration.Setup(x => x[ApplicationInsightsConfigurationName]).Returns(instrumentationKey);
            configuration.Setup(x => x[AzureMonitorConfigurationName]).Returns(connectionString);

            IList<ServiceDescriptor> serviceDescriptors = new List<ServiceDescriptor>();
            Mock<IServiceCollection> services = new Mock<IServiceCollection>();
            services.Setup(x => x.Count).Returns(serviceDescriptors.Count);
            services.Setup(x => x[It.IsAny<int>()]).Returns((int i) => serviceDescriptors[i]);
            services.Setup(x => x.Add(It.IsAny<ServiceDescriptor>())).Callback<ServiceDescriptor>((ServiceDescriptor descriptor) => serviceDescriptors.Add(descriptor));

            Startup startup = new Startup(configuration.Object);
            var addAppInsightsTelemetryMethod = startup.GetType().GetMethod(AddApplicationInsightsTelemetryMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addAppInsightsTelemetryMethod.Invoke(startup, new object[] { services.Object });
            Assert.Empty(serviceDescriptors);

            serviceDescriptors.Clear();
            var addOpenTelemetryMethod = startup.GetType().GetMethod(AddAzureMonitorOpenTelemetryMethodName, BindingFlags.NonPublic | BindingFlags.Instance);
            addOpenTelemetryMethod.Invoke(startup, new object[] { services.Object });
            Assert.NotEmpty(serviceDescriptors.Where(descriptor =>
                descriptor.ImplementationType != null ? descriptor.ImplementationType.FullName.Contains("OpenTelemetry", StringComparison.OrdinalIgnoreCase) : false));
            Assert.Empty(serviceDescriptors.Where(descriptor =>
                descriptor.ImplementationType == typeof(ApplicationInsightsLoggerProvider)
                || descriptor.ImplementationType == typeof(CloudRoleNameTelemetryInitializer)
                || descriptor.ImplementationType == typeof(UserAgentHeaderTelemetryInitializer)));
        }
    }
}
