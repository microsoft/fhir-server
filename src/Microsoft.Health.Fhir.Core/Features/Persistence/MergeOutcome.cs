// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class MergeOutcome
    {
        public MergeOutcome(MergeOutcomeFinalState state, IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> results)
        {
            State = state;
            Results = EnsureArg.IsNotNull(results, nameof(results));
        }

        public static MergeOutcome Empty { get; } = new MergeOutcome(
            MergeOutcomeFinalState.Unchanged,
            new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>());

        /// <summary>
        /// Final state of the merge operation.
        /// </summary>
        public MergeOutcomeFinalState State { get; }

        /// <summary>
        /// Set of affected records and the results of their operations.
        /// </summary>
        public IDictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome> Results { get; }

        /// <summary>
        /// Total number of affected records.
        /// </summary>
        public int Count => Results.Count;
    }
}
