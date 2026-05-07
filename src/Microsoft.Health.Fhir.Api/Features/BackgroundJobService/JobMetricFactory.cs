// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Api.Features.BackgroundJobService
{
    /// <summary>
    /// Assigns dynamic instances of IJobMetric (Metric Handlers) to jobs, based on the type of the job.
    /// </summary>
    public sealed class JobMetricFactory : IJobMetricFactory
    {
        private readonly IBulkDeleteMetricHandler _bulkDeleteMetricHandler;
        private readonly IBulkUpdateMetricHandler _bulkUpdateMetricHandler;
        private readonly IExportMetricHandler _exportMetricHandler;
        private readonly IImportMetricHandler _importMetricHandler;
        private readonly IReindexMetricHandler _reindexMetricHandler;

        public JobMetricFactory(
            IBulkDeleteMetricHandler bulkDeleteMetricHandler,
            IBulkUpdateMetricHandler bulkUpdateMetricHandler,
            IExportMetricHandler exportMetricHandler,
            IImportMetricHandler importMetricHandler,
            IReindexMetricHandler reindexMetricHandler)
        {
            _bulkDeleteMetricHandler = EnsureArg.IsNotNull(bulkDeleteMetricHandler, nameof(bulkDeleteMetricHandler));
            _bulkUpdateMetricHandler = EnsureArg.IsNotNull(bulkUpdateMetricHandler, nameof(bulkUpdateMetricHandler));
            _exportMetricHandler = EnsureArg.IsNotNull(exportMetricHandler, nameof(exportMetricHandler));
            _importMetricHandler = EnsureArg.IsNotNull(importMetricHandler, nameof(importMetricHandler));
            _reindexMetricHandler = EnsureArg.IsNotNull(reindexMetricHandler, nameof(reindexMetricHandler));
        }

        public IJobMetric Create(JobInfo jobInfo)
        {
            int jobTypeId = jobInfo.GetJobTypeId() ?? -1;

            IJobMetric jobMetric = jobTypeId switch
            {
                (int)JobType.BulkDeleteOrchestrator => _bulkDeleteMetricHandler,

                (int)JobType.BulkUpdateOrchestrator => _bulkUpdateMetricHandler,

                (int)JobType.ExportOrchestrator => _exportMetricHandler,

                (int)JobType.ImportOrchestrator => _importMetricHandler,

                (int)JobType.ReindexOrchestrator => _reindexMetricHandler,

                _ => null,
            };

            return jobMetric;
        }
    }
}
