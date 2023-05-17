// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Bundle)]
    public class DataStoreOperationIdentifierTests
    {
        private IModelInfoProvider modelInfoProvider;

        public DataStoreOperationIdentifierTests()
        {
            modelInfoProvider = Substitute.For<IModelInfoProvider>();
            modelInfoProvider.IsKnownResource(Arg.Any<string>()).Returns(true);

            ModelInfoProvider.SetProvider(modelInfoProvider);
        }

        [Fact]
        public void GivenADataStoreOperationIdentifierDictionary_WhenRunningRegularOperations_EverythingShouldWorkAsExpected()
        {
            var identifier1 = new DataStoreOperationIdentifier(
                "2112",
                "Patient",
                "1",
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false);

            var dictionary = new Dictionary<DataStoreOperationIdentifier, int>();
            dictionary.Add(identifier1, 0);

            Assert.True(dictionary.ContainsKey(identifier1));

            var identifier2 = new DataStoreOperationIdentifier(
                "2112",
                "Patient",
                "1",
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false);

            Assert.True(dictionary.ContainsKey(identifier2));
        }

        [Fact]
        public void GivenTwoDataStoreOperationIdentifiersWithTheSameValues_WhenCompared_BothShouldBeEqual()
        {
            var identifier1 = new DataStoreOperationIdentifier(
                "2112",
                "Patient",
                "1",
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false);

            var identifier2 = new DataStoreOperationIdentifier(
                "2112",
                "Patient",
                "1",
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false);

            Assert.Equal(identifier1, identifier2);
            Assert.Equal(identifier1.GetHashCode(), identifier2.GetHashCode());
        }
    }
}
