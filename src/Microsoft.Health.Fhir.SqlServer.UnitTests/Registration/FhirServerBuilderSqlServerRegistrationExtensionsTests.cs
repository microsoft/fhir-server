// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
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

        [Fact]
        public void GivenCoreJobsAlreadyRegistered_WhenAddingSqlServer_ThenCoreJobsAreNotRegisteredAgain()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            RegisterCoreJobs(services);

            var builder = new TestFhirServerBuilder(services);

            builder.AddSqlServer(_ => { });

            int coreJobCount = CountJobTypesInSameAssemblyAs<BulkUpdateOrchestratorJob>();
            int sqlServerJobCount = CountJobTypesInSameAssemblyAs<ImportOrchestratorJob>();
            int registeredJobFactoryCount = services.Count(service => service.ServiceType == typeof(Func<IJob>));

            Assert.Equal(coreJobCount + sqlServerJobCount, registeredJobFactoryCount);
        }

        private static void RegisterCoreJobs(IServiceCollection services)
        {
            foreach (TypeRegistrationBuilder job in services.TypesInSameAssemblyAs<BulkUpdateOrchestratorJob>()
                .AssignableTo<IJob>()
                .Transient()
                .AsSelf())
            {
                job.AsDelegate<Func<IJob>>();
            }
        }

        private static int CountJobTypesInSameAssemblyAs<TJob>()
        {
            return typeof(TJob).Assembly
                .GetTypes()
                .Count(type => typeof(IJob).IsAssignableFrom(type) && type.IsClass && !type.IsAbstract);
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
