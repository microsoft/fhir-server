// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Reindex
{
    public class ReindexJobSqlThrottlingController : IReindexJobThrottleController
    {
        private uint _targetBatchSize;

        public int GetThrottleBasedDelay()
        {
            return 0;
        }

        public void Initialize(ReindexProcessingJobDefinition reindexJobRecord, int? provisionedDatastoreCapacity)
        {
            _targetBatchSize = reindexJobRecord.MaximumNumberOfResourcesPerQuery;
            return;
        }

        public double UpdateDatastoreUsage()
        {
            return 0.0;
        }

        public uint GetThrottleBatchSize()
        {
            return _targetBatchSize;
        }
    }
}
