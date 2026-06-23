// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema.Messages.Notifications;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class FhirServerBuilderSqlServerRegistrationExtensionsTests
    {
        [Fact]
        public void GivenSqlServerBuilder_WhenAddingSqlServer_ThenSchemaUpgradeHandlerIsRegistered()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

            var builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(_ => { });

            Assert.Contains(
                services,
                service =>
                    service.ServiceType == typeof(INotificationHandler<SchemaUpgradedNotification>) &&
                    service.ImplementationType == typeof(SchemaUpgradedHandler));
        }

        private sealed class TestFhirServerBuilder : IFhirServerBuilder
        {
            public TestFhirServerBuilder(IServiceCollection services)
            {
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
