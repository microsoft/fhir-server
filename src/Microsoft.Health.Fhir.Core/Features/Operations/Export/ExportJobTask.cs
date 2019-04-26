// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportJobTask
    {
        private readonly ExportJobRecord _exportJobRecord;
        private readonly IFhirOperationsDataStore _fhirOperationsDataStore;
        private readonly ILogger _logger;

        private WeakETag _weakETag;

        public ExportJobTask(
            ExportJobRecord exportJobRecord,
            WeakETag weakETag,
            IFhirOperationsDataStore fhirOperationsDataStore,
            ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));
            EnsureArg.IsNotNull(weakETag, nameof(weakETag));
            EnsureArg.IsNotNull(fhirOperationsDataStore, nameof(fhirOperationsDataStore));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportJobRecord = exportJobRecord;
            _fhirOperationsDataStore = fhirOperationsDataStore;
            _logger = logger;

            _weakETag = weakETag;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Try to acquire the job.
                try
                {
                    _logger.LogTrace("Acquiring the job.");

                    await UpdateJobStatus(OperationStatus.Running, cancellationToken);
                }
                catch (JobConflictException)
                {
                    // The job is taken by another process.
                    _logger.LogWarning("Failed to acquire the job. The job was acquired by another process.");
                    return;
                }

                // We have acquired the job, process the export.
                _logger.LogTrace("Successfully completed the job.");

                await UpdateJobStatus(OperationStatus.Completed, cancellationToken);
            }
            catch (Exception ex)
            {
                // The job has encountered an error it cannot recover from.
                // Try to update the job to failed state.
                _logger.LogError(ex, "Encountered an unhandled exception. The job will be marked as failed.");

                await UpdateJobStatus(OperationStatus.Failed, cancellationToken);
            }
        }

        private async Task UpdateJobStatus(OperationStatus operationStatus, CancellationToken cancellationToken)
        {
            _exportJobRecord.Status = operationStatus;

            ExportJobOutcome updatedExportJobOutcome = await _fhirOperationsDataStore.UpdateExportJobAsync(_exportJobRecord, _weakETag, cancellationToken);

            _weakETag = updatedExportJobOutcome.ETag;
        }
    }
}
