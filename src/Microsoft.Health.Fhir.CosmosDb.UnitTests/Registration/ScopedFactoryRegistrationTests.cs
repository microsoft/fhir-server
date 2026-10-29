// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ScopedFactoryRegistrationTests
    {
        [Fact]
        public void GivenRootResolvedQueueFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            IServiceCollection services = CreateServices();
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<IQueueClient>(provider, nameof(IQueueClient));
        }

        [Fact]
        public void GivenQueueFactory_WhenInvokedTwice_ThenCosmosQueueClientsAreScopedIndependently()
        {
            IServiceCollection services = CreateServices();
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            Func<IScoped<IQueueClient>> factory = provider.GetRequiredService<Func<IScoped<IQueueClient>>>();
            using IScoped<IQueueClient> first = factory();
            using IScoped<IQueueClient> second = factory();
            CosmosQueueClient firstPayload = Assert.IsType<CosmosQueueClient>(first.Value);
            CosmosQueueClient secondPayload = Assert.IsType<CosmosQueueClient>(second.Value);

            Assert.NotSame(first, second);
            Assert.NotSame(firstPayload, secondPayload);
            Assert.True(firstPayload.IsInitialized());
            Assert.True(secondPayload.IsInitialized());

            first.Dispose();

            Assert.Throws<ObjectDisposedException>(() => first.Value);
            Assert.Same(secondPayload, second.Value);
            Assert.True(second.Value.IsInitialized());

            second.Dispose();

            Assert.Throws<ObjectDisposedException>(() => second.Value);
        }

        private static IServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddScoped();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<RequestContextAccessor<IFhirRequestContext>, FhirRequestContextAccessor>();
            services.AddSingleton(Substitute.For<IMediator>());
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddCosmosDb();
            return services;
        }
    }
}
