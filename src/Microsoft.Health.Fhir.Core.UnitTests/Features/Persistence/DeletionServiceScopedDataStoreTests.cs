// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence
{
    public sealed class DeletionServiceScopedDataStoreTests
    {
        [Fact]
        public void Ctor_WhenArgumentIsNull_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DeletionServiceScopedDataStore(dataStore: null));

            Assert.Throws<ArgumentNullException>(() => new DeletionServiceScopedDataStore(dataStoreScopeProvider: null));
        }

        [Fact]
        public void DataStore_ReturnsScopedValue()
        {
            var scopeProvider = Substitute.For<IScopeProvider<IFhirDataStore>>();
            var deletionServiceScopedDataStore = new DeletionServiceScopedDataStore(scopeProvider);

            var dataStore = Substitute.For<IFhirDataStore>();
            var scopedDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            scopedDataStore.Value.Returns(dataStore);

            scopeProvider.Invoke().Returns(scopedDataStore);

            var result1 = deletionServiceScopedDataStore.GetDataStore();
            Assert.Same(dataStore, result1);

            var result2 = deletionServiceScopedDataStore.GetDataStore();
            Assert.Same(dataStore, result2);
        }

        [Fact]
        public void DataStore_ReturnsRegularFhirDataStore()
        {
            var dataStore = Substitute.For<IFhirDataStore>();
            var deletionServiceScopedDataStore = new DeletionServiceScopedDataStore(dataStore);

            var result1 = deletionServiceScopedDataStore.GetDataStore();
            Assert.Same(dataStore, result1);

            var result2 = deletionServiceScopedDataStore.GetDataStore();
            Assert.Same(dataStore, result2);
        }

        [Fact]
        public void Dispose_DisposesScopedDataStore()
        {
            var scopedDataStore = Substitute.For<IScoped<IFhirDataStore>>();
            var scopeProvider = Substitute.For<IScopeProvider<IFhirDataStore>>();
            scopeProvider.Invoke().Returns(scopedDataStore);

            var deletionServiceScopedDataStore = new DeletionServiceScopedDataStore(scopeProvider);

            var dataStore = deletionServiceScopedDataStore.GetDataStore();

            deletionServiceScopedDataStore.Dispose();

            scopedDataStore.Received(1).Dispose();
        }
    }
}
