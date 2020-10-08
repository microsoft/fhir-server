// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IBatchedUpsertFhirDataStore : IFhirDataStore
    {
        Task<IReadOnlyCollection<UpsertOutcome>> UpsertBatchAsync(
            IReadOnlyList<(ResourceWrapper resourceWrapper, WeakETag weakEtag, bool allowCreate, bool keepHistory)> readOnlyList,
            DateTime commonLastUpdateDateTime,
            CancellationToken cancellationToken);
    }
}
