// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Reindex
{
    public class ReindexJobSqlThrottlingController : IReindexJobThrottleController
    {
        public int GetThrottleBasedDelay()
        {
            return 0;
        }

        public void Initialize(ReindexJobRecord reindexJobRecord, int? provisionedDatastoreCapacity)
        {
            return;
        }

        public void UpdateDatastoreUsage()
        {
            return;
        }
    }
}
