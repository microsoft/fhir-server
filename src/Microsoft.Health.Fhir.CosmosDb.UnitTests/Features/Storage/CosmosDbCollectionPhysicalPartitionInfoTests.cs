// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosDbCollectionPhysicalPartitionInfoTests
    {
        [Fact]
        public void GivenPhysicalPartitionInfo_WhenCheckingIfAKeyIsResourceToken_ThenReturnFalseToAllInvalidKeys()
        {
            Assert.False(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken("test"));
            Assert.False(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken(string.Empty));
            Assert.False(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken(null));
            Assert.False(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken("_type=resource&")); // The key must start with "type=resource&" to be considered a resource token.
        }

        [Fact]
        public void GivenPhysicalPartitionInfo_WhenCheckingIfAKeyIsResourceToken_ThenReturnTrueToAllValidKeys()
        {
            Assert.True(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken("type=resource&"));
            Assert.True(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken("type=resource&1234567890"));
            Assert.True(CosmosDbCollectionPhysicalPartitionInfo.IsResourceToken("type=resource&abcdefghi"));
        }
    }
}
