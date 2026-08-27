// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    public class FhirStorageTestsFixtureTests
    {
        [Fact]
        public async Task GivenNestedAsyncFixtureAndOwnedServiceProvider_WhenDisposedAsync_ThenEachCleanupRunsOnce()
        {
            // Arrange - the nested fixture matches the real Sql/Cosmos shape: IServiceProvider + IAsyncLifetime, no IDisposable.
            IServiceProvider nestedFixture = Substitute.For<IServiceProvider, IAsyncLifetime>();
            IAsyncDisposable ownedServiceProvider = Substitute.For<IAsyncDisposable>();
            IFhirRuntimeConfiguration runtimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            var fixture = new FhirStorageTestsFixture(nestedFixture, runtimeConfiguration, ownedServiceProvider);

            // Act
            await fixture.DisposeAsync();

            // Assert - each cleanup runs exactly once.
            await ((IAsyncLifetime)nestedFixture).Received(1).DisposeAsync();
            await ownedServiceProvider.Received(1).DisposeAsync();
        }

        [Fact]
        public async Task GivenNestedAsyncCleanupThrows_WhenDisposedAsync_ThenExceptionPropagatesAndOwnedProviderStillDisposedOnce()
        {
            // Arrange - the nested fixture's async cleanup faults with a specific exception.
            var expectedException = new InvalidOperationException("nested fixture cleanup failed");
            IServiceProvider nestedFixture = Substitute.For<IServiceProvider, IAsyncLifetime>();
            ((IAsyncLifetime)nestedFixture).DisposeAsync().Returns(ValueTask.FromException(expectedException));
            IAsyncDisposable ownedServiceProvider = Substitute.For<IAsyncDisposable>();
            IFhirRuntimeConfiguration runtimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            var fixture = new FhirStorageTestsFixture(nestedFixture, runtimeConfiguration, ownedServiceProvider);

            // Act
            InvalidOperationException actualException = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.DisposeAsync().AsTask());

            // Assert - the nested failure propagates, and the owned provider is disposed in finally.
            Assert.Same(expectedException, actualException);
            await ownedServiceProvider.Received(1).DisposeAsync();
        }
    }
}
