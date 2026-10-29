// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Medino;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Queues;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Scoping;
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
        public async Task GivenRootResolvedContainerFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            IServiceCollection services = CreateServices();
            Container container = Substitute.For<Container>();
            CosmosClient client = Substitute.For<CosmosClient>();
            var initializer = Substitute.For<ICosmosClientInitializer>();
            initializer.CreateCosmosClient(Arg.Any<CosmosDataStoreConfiguration>()).Returns(client);
            initializer.CreateFhirContainer(Arg.Any<CosmosClient>(), Arg.Any<string>(), Arg.Any<string>()).Returns(container);
            var configuration = Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>();
            configuration.Get(Arg.Any<string>()).Returns(new CosmosCollectionConfiguration());
            var lockFactory = Substitute.For<ICosmosDbDistributedLockFactory>();
            lockFactory.Create(container, Arg.Any<string>()).Returns(Substitute.For<ICosmosDbDistributedLock>());
            services.AddSingleton(_ => new CosmosContainerProvider(
                Substitute.For<IFhirRuntimeConfiguration>(),
                new CosmosDataStoreConfiguration { AllowCollectionSetup = false, AllowDatabaseCreation = false },
                configuration,
                initializer,
                Substitute.For<ICollectionSetup>(),
                Substitute.For<ICollectionDataUpdater>(),
                new RetryExceptionPolicyFactory(
                    new CosmosDataStoreConfiguration(),
                    new FhirRequestContextAccessor(),
                    NullLogger<RetryExceptionPolicyFactory>.Instance),
                NullLogger<CosmosContainerProvider>.Instance,
                Substitute.For<IMediator>(),
                Array.Empty<ICollectionInitializer>(),
                lockFactory,
                Substitute.For<ICosmosClientTestProvider>()));
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            await provider.GetRequiredService<CosmosContainerProvider>().EnsureInitialized();

            ScopedFactoryAssert.NotRetainedByRoot<Container>(provider, nameof(Container));

            IScopeProvider<Container> factory = provider.GetRequiredService<IScopeProvider<Container>>();
            using (IScoped<Container> first = factory.Invoke())
            using (IScoped<Container> second = factory.Invoke())
            {
                Assert.NotSame(first, second);
                Assert.Same(container, first.Value);
                Assert.Same(container, second.Value);
                first.Dispose();
                Assert.Same(container, second.Value);
            }

            client.DidNotReceive().Dispose();
            provider.Dispose();
            client.Received(1).Dispose();
        }

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
            IScopeProvider<IQueueClient> factory = provider.GetRequiredService<IScopeProvider<IQueueClient>>();
            using IScoped<IQueueClient> first = factory.Invoke();
            using IScoped<IQueueClient> second = factory.Invoke();
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
