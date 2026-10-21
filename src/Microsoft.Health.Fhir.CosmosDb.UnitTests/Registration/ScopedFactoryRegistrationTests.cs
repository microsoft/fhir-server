// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
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
        public void GivenQueueFactory_WhenInvokedTwice_ThenPayloadsAreScopedAndDisposedIndependently()
        {
            IServiceCollection services = CreateServices();
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            Func<IScoped<IQueueClient>> factory = provider.GetRequiredService<Func<IScoped<IQueueClient>>>();
            using IScoped<IQueueClient> first = factory();
            using IScoped<IQueueClient> second = factory();
            var firstPayload = (IDisposable)first.Value;
            var secondPayload = (IDisposable)second.Value;

            Assert.NotSame(first.Value, second.Value);
            firstPayload.DidNotReceive().Dispose();
            secondPayload.DidNotReceive().Dispose();

            first.Dispose();

            firstPayload.Received(1).Dispose();
            secondPayload.DidNotReceive().Dispose();

            second.Dispose();

            firstPayload.Received(1).Dispose();
            secondPayload.Received(1).Dispose();
        }

        private static IServiceCollection CreateServices()
        {
            var services = new ServiceCollection();
            services.AddScoped();
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddCosmosDb();
            services.AddScoped(_ => Substitute.For<IQueueClient, IDisposable>());
            return services;
        }
    }
}
