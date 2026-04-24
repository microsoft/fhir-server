// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.BulkDelete)]
    public class BulkOperationAuditLogHelperTests
    {
        private const int MaxAffectedItemsSize = BulkOperationAuditLogHelper.MaxAuditLogSize - BulkOperationAuditLogHelper.AuditLogOverheadSize;

        [Fact]
        public void GivenEmptyItemsList_WhenCreatingBatches_ThenSingleEmptyBatchIsReturned()
        {
            var items = new List<(string resourceType, string resourceId, bool included)>();

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

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

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

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

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

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

            for (int i = 0; ; i++)
            {
                string nextItem = (i == 0 ? string.Empty : ", ") + "Patient/id" + i;
                if (currentLength + nextItem.Length > MaxAffectedItemsSize)
                {
                    break;
                }

                items.Add(("Patient", "id" + i, false));
                currentLength += nextItem.Length;
            }

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

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

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

            Assert.Single(batches);
            Assert.Contains("[Include] Patient/1", batches[0]);
            Assert.DoesNotContain("[Include] Observation/2", batches[0]);
            Assert.Contains("Observation/2", batches[0]);
            Assert.Contains("[Include] Patient/3", batches[0]);
        }

        [Fact]
        public void GivenBatchesSplit_WhenCreatingBatches_ThenNoBatchStartsWithComma()
        {
            // Create enough items to force multiple batches
            var items = new List<(string resourceType, string resourceId, bool included)>();
            for (int i = 0; i < 1000; i++)
            {
                items.Add(("Patient", $"resource-id-{i:D10}", false));
            }

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

            Assert.True(batches.Count > 1);

            // No batch should start with ", "
            foreach (string batch in batches)
            {
                Assert.False(batch.StartsWith(", "), "Batch should not start with a leading comma separator.");
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

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

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

            IReadOnlyList<string> batches = BulkOperationAuditLogHelper.CreateAffectedItemBatches(items);

            // Verify no items are lost by counting occurrences
            string allContent = string.Concat(batches);
            for (int i = 0; i < 2000; i++)
            {
                Assert.Contains($"Patient/id-{i}", allContent);
            }
        }

        internal static async Task WaitForAuditLogCall(IAuditLogger auditLogger, int timeoutMs = 5000, int pollIntervalMs = 50)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                try
                {
                    auditLogger.Received().LogAudit(
                        Arg.Any<AuditAction>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<Uri>(),
                        Arg.Any<HttpStatusCode?>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                        Arg.Any<IReadOnlyDictionary<string, string>>(),
                        Arg.Any<string>(),
                        Arg.Any<string>(),
                        Arg.Is<IReadOnlyDictionary<string, string>>(d => d.ContainsKey("Affected Items")));
                    return;
                }
                catch (NSubstitute.Exceptions.ReceivedCallsException)
                {
                    await Task.Delay(pollIntervalMs);
                    elapsed += pollIntervalMs;
                }
            }

            Assert.Fail("Timed out waiting for expected call to AuditLogger.LogAudit with 'Affected Items' property.");
        }
    }
}
