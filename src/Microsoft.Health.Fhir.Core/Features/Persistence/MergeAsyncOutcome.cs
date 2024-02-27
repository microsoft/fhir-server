// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class MergeAsyncOutcome
    {
        public MergeAsyncOutcome(IReadOnlyDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> resources, bool isPerformanceAffectedByConflicts)
        {
            EnsureArg.IsNotNull(resources, nameof(resources));

            Resources = resources;
            IsPerformanceAffectedByConflicts = isPerformanceAffectedByConflicts;
        }

        public IReadOnlyDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> Resources { get; }

        public bool IsPerformanceAffectedByConflicts { get; }
    }
}
