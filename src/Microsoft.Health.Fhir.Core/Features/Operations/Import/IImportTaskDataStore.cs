// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public interface IImportTaskDataStore
    {
        /// <summary>
        /// Get import progress from sub tasks
        /// </summary>
        /// <param name="queueId">task queue id</param>
        /// <param name="taskId">import task id</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public Task<IEnumerable<string>> GetImportProcessingTaskResultAsync(string queueId, string taskId, CancellationToken cancellationToken);
    }
}
