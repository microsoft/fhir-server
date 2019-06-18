// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class OrderedSetOfBlockIdsTests
    {
        [Fact]
        public void GivenOrderedSetOfBlockIds_WhenAddingExistingItem_ThenItemDoesNotGetAdded()
        {
            string item1 = "item1";
            string item2 = "item2";

            var orderedSetOfBlockIds = new OrderedSetOfBlockIds();
            orderedSetOfBlockIds.Add(item1);
            orderedSetOfBlockIds.Add(item1);
            orderedSetOfBlockIds.Add(item2);

            var result = orderedSetOfBlockIds.ToList();

            Assert.Equal(2, result.Count);
            Assert.Contains(item1, result);
            Assert.Contains(item2, result);
        }

        [Fact]
        public void GivenListContainingDuplicateItems_WhenCreatingOrderedSetOfBlockIds_ThenOnlyDistinctItemsAreAdded()
        {
            string item1 = "item1";
            string item2 = "item2";
            var input = new List<string>() { item1, item2, item2, item2, item1 };

            var orderedSetOfBlockIds = new OrderedSetOfBlockIds(input);

            var result = orderedSetOfBlockIds.ToList();

            Assert.Equal(2, result.Count);
            Assert.Contains(item1, result);
            Assert.Contains(item2, result);
        }
    }
}
