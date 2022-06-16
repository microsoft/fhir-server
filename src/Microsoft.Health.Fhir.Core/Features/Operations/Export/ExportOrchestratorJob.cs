// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportOrchestratorJob : IJob
    {
        private JobInfo _jobInfo;
        private IQueueClient _queueClient;
        private ILogger<ExportOrchestratorJob> _logger;

        public ExportOrchestratorJob(
            JobInfo jobInfo,
            IQueueClient queueClient,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(jobInfo, nameof(jobInfo));
            EnsureArg.IsNotNull(queueClient, nameof(queueClient));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _jobInfo = jobInfo;
            _queueClient = queueClient;
            _logger = loggerFactory.CreateLogger<ExportOrchestratorJob>();
        }

        public async Task<string> ExecuteAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            
            _queueClient.EnqueueAsync(QueueType.Export, definitions, _jobInfo.GroupId, false, false, cancellationToken);
            // If the filter attribute is null and it isn't patient or group export...
                // Call the SQL stored procedure to get resource surogate ids based on start and end time
                // Make a batch of export processing jobs
                // Repeat if needed
            // else
                // Make one processing job for the entire export
        }
    }
}
