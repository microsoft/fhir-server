// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Tenancy
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class TenantSharedServiceRegistryTests
    {
        [Fact]
        public void GivenAnEmptyRegistry_WhenQueried_ThenNothingIsShared()
        {
            var registry = new TenantSharedServiceRegistry();

            Assert.False(registry.IsShared(typeof(ILoggerFactory)));
            Assert.Empty(registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenARegisteredType_WhenQueried_ThenItIsShared()
        {
            var registry = new TenantSharedServiceRegistry();
            registry.ShareWithTenants<ILoggerFactory>();

            Assert.True(registry.IsShared(typeof(ILoggerFactory)));
            Assert.Contains(typeof(ILoggerFactory), registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenATypeBasedRegistration_WhenChained_ThenItReturnsTheSameRegistryAndRegistersTheType()
        {
            var registry = new TenantSharedServiceRegistry();

            TenantSharedServiceRegistry chainedRegistry = registry.ShareWithTenants(typeof(ILoggerFactory));
            chainedRegistry.ShareWithTenants(typeof(ILoggerProvider));

            Assert.Same(registry, chainedRegistry);
            Assert.True(registry.IsShared(typeof(ILoggerFactory)));
            Assert.True(registry.IsShared(typeof(ILoggerProvider)));
            Assert.Contains(typeof(ILoggerFactory), registry.SharedServiceTypes);
            Assert.Contains(typeof(ILoggerProvider), registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenADuplicateRegistration_WhenQueried_ThenItAppearsOnce()
        {
            var registry = new TenantSharedServiceRegistry();
            registry.ShareWithTenants<ILoggerFactory>();
            registry.ShareWithTenants<ILoggerFactory>();

            Assert.Single(registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenANullType_WhenShared_ThenAnArgumentExceptionIsThrown()
        {
            var registry = new TenantSharedServiceRegistry();

            Assert.Throws<ArgumentNullException>(() => registry.ShareWithTenants((Type)null));
        }

        [Fact]
        public void GivenTheHostedServiceInterface_WhenShared_ThenAnExceptionExplainsHostedServicesMustUseThePolicy()
        {
            var registry = new TenantSharedServiceRegistry();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => registry.ShareWithTenants<IHostedService>());

            Assert.Contains(typeof(IHostedService).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(ITenantHostedServicePolicy).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Empty(registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenAConcreteHostedService_WhenShared_ThenAnExceptionExplainsHostedServicesMustUseThePolicy()
        {
            var registry = new TenantSharedServiceRegistry();

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => registry.ShareWithTenants<TestHostedService>());

            Assert.Contains(typeof(TestHostedService).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Contains(typeof(ITenantHostedServicePolicy).FullName, exception.Message, StringComparison.Ordinal);
            Assert.Empty(registry.SharedServiceTypes);
        }

        [Fact]
        public void GivenSharedServiceTypesSnapshot_WhenMutationIsAttempted_ThenItIsRejectedAndRegistryRemainsUnchanged()
        {
            var registry = new TenantSharedServiceRegistry();
            registry.ShareWithTenants<ILoggerFactory>();

            IReadOnlyCollection<Type> sharedServiceTypes = registry.SharedServiceTypes;
            ICollection<Type> mutableView = Assert.IsAssignableFrom<ICollection<Type>>(sharedServiceTypes);

            Assert.True(mutableView.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => mutableView.Add(typeof(ILoggerProvider)));

            Assert.Single(registry.SharedServiceTypes);
            Assert.True(registry.IsShared(typeof(ILoggerFactory)));
            Assert.False(registry.IsShared(typeof(ILoggerProvider)));
        }

        private sealed class TestHostedService : IHostedService
        {
            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
