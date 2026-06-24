// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkUpdate;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.CosmosDb.Features.Operations.Export;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class FhirServerBuilderCosmosDbRegistrationExtensionsTests
    {
        [Fact]
        public void GivenCoreJobsAlreadyRegistered_WhenAddingCosmosDb_ThenCoreJobsAreNotRegisteredAgain()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            RegisterCoreJobs(services);

            var builder = new TestFhirServerBuilder(services);

            builder.AddCosmosDb();

            int coreJobCount = CountJobTypesInSameAssemblyAs<BulkUpdateOrchestratorJob>();
            int cosmosJobCount = CountJobTypesInSameAssemblyAs<CosmosExportOrchestratorJob>();
            int registeredJobFactoryCount = services.Count(service => service.ServiceType == typeof(Func<IJob>));

            Assert.Equal(coreJobCount + cosmosJobCount, registeredJobFactoryCount);
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
