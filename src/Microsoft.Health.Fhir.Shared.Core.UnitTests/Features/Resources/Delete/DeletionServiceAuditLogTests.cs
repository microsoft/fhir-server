// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Resources.Delete
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class DeletionServiceAuditLogTests
    {
        private const int MaxAffectedItemsSize = DeletionService.MaxAuditLogSize - DeletionService.AuditLogOverheadSize;

        [Fact]
        public void GivenEmptyItemsList_WhenCreatingBatches_ThenSingleEmptyBatchIsReturned()
        {
            var items = new List<(string resourceType, string resourceId, bool included)>();

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.Single(batches);
            Assert.Equal(string.Empty, batches[0]);
        }

        [Fact]
        public void GivenSmallItemsList_WhenCreatingBatches_ThenSingleBatchContainsAllItems()
        {
            var items = new List<(string resourceType, string resourceId, bool included)>
            {
                ("Patient", "123", false),
                ("Observation", "456", false),
                ("Encounter", "789", true),
            };

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.Single(batches);
            Assert.Contains("Patient/123", batches[0]);
            Assert.Contains("Observation/456", batches[0]);
            Assert.Contains("[Include] Encounter/789", batches[0]);
        }

        [Fact]
        public void GivenLargeItemsList_WhenCreatingBatches_ThenMultipleBatchesAreCreated()
        {
            // Create enough items to exceed the max size
            var items = new List<(string resourceType, string resourceId, bool included)>();
            for (int i = 0; i < 1000; i++)
            {
                items.Add(("Patient", $"resource-id-{i:D10}", false));
            }

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.True(batches.Count > 1, "Expected multiple batches for a large items list.");

            // Verify each batch is within the size limit
            foreach (string batch in batches)
            {
                Assert.True(batch.Length <= MaxAffectedItemsSize, $"Batch length {batch.Length} exceeds max size {MaxAffectedItemsSize}.");
            }

            // Verify all items are present across all batches
            string allBatches = string.Concat(batches);
            for (int i = 0; i < 1000; i++)
            {
                Assert.Contains($"Patient/resource-id-{i:D10}", allBatches);
            }
        }

        [Fact]
        public void GivenItemsExactlyAtLimit_WhenCreatingBatches_ThenSingleBatchIsReturned()
        {
            // Build items that fit exactly within the limit
            var items = new List<(string resourceType, string resourceId, bool included)>();
            int currentLength = 0;

            while (true)
            {
                string nextItem = ", Patient/id" + items.Count;
                if (currentLength + nextItem.Length > MaxAffectedItemsSize)
                {
                    break;
                }

                items.Add(("Patient", "id" + items.Count, false));
                currentLength += nextItem.Length;
            }

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.Single(batches);
            Assert.True(batches[0].Length <= MaxAffectedItemsSize);
        }

        [Fact]
        public void GivenIncludedItems_WhenCreatingBatches_ThenIncludeTagIsPreserved()
        {
            var items = new List<(string resourceType, string resourceId, bool included)>
            {
                ("Patient", "1", true),
                ("Observation", "2", false),
                ("Patient", "3", true),
            };

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.Single(batches);
            Assert.Contains("[Include] Patient/1", batches[0]);
            Assert.DoesNotContain("[Include] Observation/2", batches[0]);
            Assert.Contains("Observation/2", batches[0]);
            Assert.Contains("[Include] Patient/3", batches[0]);
        }

        [Fact]
        public void GivenBatchesSplit_WhenCreatingBatches_ThenEachBatchStartsWithComma()
        {
            // Create enough items to force multiple batches
            var items = new List<(string resourceType, string resourceId, bool included)>();
            for (int i = 0; i < 1000; i++)
            {
                items.Add(("Patient", $"resource-id-{i:D10}", false));
            }

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.True(batches.Count > 1);

            // Each batch should start with ", " to maintain consistent formatting
            foreach (string batch in batches)
            {
                Assert.StartsWith(", ", batch);
            }
        }

        [Fact]
        public void GivenSingleLargeItem_WhenCreatingBatches_ThenItemIsInOwnBatch()
        {
            // A single item with a very long ID - should still be included even if it exceeds max
            string longId = new string('x', MaxAffectedItemsSize);
            var items = new List<(string resourceType, string resourceId, bool included)>
            {
                ("Patient", longId, false),
            };

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            Assert.Single(batches);
            Assert.Contains($"Patient/{longId}", batches[0]);
        }

        [Fact]
        public void GivenItemsThatCauseSplit_WhenCreatingBatches_ThenNoItemsAreLost()
        {
            var items = new List<(string resourceType, string resourceId, bool included)>();
            for (int i = 0; i < 2000; i++)
            {
                items.Add(("Patient", $"id-{i}", i % 3 == 0));
            }

            IList<string> batches = DeletionService.CreateAffectedItemBatches(items);

            // Verify no items are lost by counting occurrences
            string allContent = string.Concat(batches);
            for (int i = 0; i < 2000; i++)
            {
                Assert.Contains($"Patient/id-{i}", allContent);
            }
        }
    }
}
