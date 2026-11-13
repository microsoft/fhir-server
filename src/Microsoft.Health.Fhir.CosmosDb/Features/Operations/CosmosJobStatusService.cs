// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Operations
{
    /// <summary>
    /// Cosmos DB implementation of the job status service.
    /// This feature is not supported on Cosmos DB.
    /// </summary>
    public class CosmosJobStatusService : IJobStatusService
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<JobStatusInfo>> GetAllJobStatusAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException(Fhir.Core.Resources.JobStatusNotSupportedForCosmosDb);
        }
    }
}
