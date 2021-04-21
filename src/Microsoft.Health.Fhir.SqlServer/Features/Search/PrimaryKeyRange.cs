// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// Represents an expanded greater than or less than a primary key position.
    /// Expands out into something like:
    /// WHERE (ResourceTypeId = currentValue.ResourceTypeId AND ResourceResourceSurrogateId > currentValue.ResourceSurrogateId)
    ///       OR ResourceTypeId IN (nextResourceTypeIds)
    /// </summary>
    internal class PrimaryKeyRange
    {
        public PrimaryKeyRange(PrimaryKeyValue currentValue, BitArray nextResourceTypeIds)
        {
            EnsureArg.IsNotNull(currentValue, nameof(currentValue));
            EnsureArg.IsNotNull(nextResourceTypeIds, nameof(nextResourceTypeIds));

            CurrentValue = currentValue;
            NextResourceTypeIds = nextResourceTypeIds;
        }

        public PrimaryKeyValue CurrentValue { get; }

        public BitArray NextResourceTypeIds { get; }

        public override string ToString()
        {
            return $"(PrimaryKeyRange {CurrentValue} (Next {string.Join(" ", NextResourceTypeIds.Cast<bool>().Select((b, i) => (i, b)).Where(t => t.b).Select(t => t.i))}))";
        }
    }
}
