// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    /// <summary>
    /// Shared helper for building size-bounded audit-log batches used by
    /// bulk delete and bulk update operations.
    /// </summary>
    internal static class BulkOperationAuditLogHelper
    {
        internal const int MaxAuditLogSize = 16000;
        internal const int AuditLogOverheadSize = 1000;

        /// <summary>
        /// Splits the affected-item list into string batches, each staying
        /// under <see cref="MaxAuditLogSize"/> minus <see cref="AuditLogOverheadSize"/> characters.
        /// Items are never split mid-entry; a single oversized item is kept
        /// in its own batch rather than truncated.
        /// </summary>
        internal static IReadOnlyList<string> CreateAffectedItemBatches(IEnumerable<(string resourceType, string resourceId, bool included)> items)
        {
            int maxAffectedItemsSize = MaxAuditLogSize - AuditLogOverheadSize;
            var batches = new List<string>();
            var currentBatch = new StringBuilder();

            foreach (var item in items)
            {
                string itemString = $"{(item.included ? "[Include] " : string.Empty)}{item.resourceType}/{item.resourceId}";
                string separator = currentBatch.Length > 0 ? ", " : string.Empty;

                if (currentBatch.Length > 0 && currentBatch.Length + separator.Length + itemString.Length > maxAffectedItemsSize)
                {
                    batches.Add(currentBatch.ToString());
                    currentBatch.Clear();
                    separator = string.Empty;
                }

                currentBatch.Append(separator);
                currentBatch.Append(itemString);
            }

            batches.Add(currentBatch.ToString());

            return batches;
        }
    }
}
