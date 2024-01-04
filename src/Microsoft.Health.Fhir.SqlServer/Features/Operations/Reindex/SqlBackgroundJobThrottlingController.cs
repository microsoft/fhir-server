// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Reindex
{
    public class SqlBackgroundJobThrottlingController : IBackgroundJobThrottleController
    {
        private uint _targetBatchSize;

        public int GetThrottleBasedDelay()
        {
            return 0;
        }

        public Task Initialize(IThrottleableJobRecord jobRecord, CancellationToken cancellationToken)
        {
            _targetBatchSize = jobRecord.MaximumNumberOfResourcesPerQuery;
            return Task.CompletedTask;
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
