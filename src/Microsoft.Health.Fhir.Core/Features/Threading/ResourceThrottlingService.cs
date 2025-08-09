// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Threading
{
    /// <summary>
    /// Service to throttle resource usage and prevent system overload.
    /// </summary>
    public sealed class ResourceThrottlingService : IResourceThrottlingService, IDisposable
    {
        private readonly SemaphoreSlim _exportSemaphore;
        private readonly SemaphoreSlim _importSemaphore;
        private readonly SemaphoreSlim _bulkUpdateSemaphore;
        private readonly ILogger<ResourceThrottlingService> _logger;
        private readonly IDynamicThreadingService _threadingService;

        public ResourceThrottlingService(
            IOptions<ExportJobConfiguration> exportConfig,
            IOptions<ImportJobConfiguration> importConfig,
            IDynamicThreadingService threadingService,
            ILogger<ResourceThrottlingService> logger)
        {
            _threadingService = threadingService;
            _logger = logger;

            // Use adaptive values if configuration is 0 or negative
            var maxExportOps = exportConfig.Value.MaxConcurrentExportOperations > 0
                ? exportConfig.Value.MaxConcurrentExportOperations
                : _threadingService.GetMaxConcurrentOperations(OperationType.Export);

            var maxImportOps = importConfig.Value.MaxConcurrentImportOperations > 0
                ? importConfig.Value.MaxConcurrentImportOperations
                : _threadingService.GetMaxConcurrentOperations(OperationType.Import);

            var maxBulkUpdateOps = _threadingService.GetMaxConcurrentOperations(OperationType.BulkUpdate);

            _exportSemaphore = new SemaphoreSlim(maxExportOps, maxExportOps);
            _importSemaphore = new SemaphoreSlim(maxImportOps, maxImportOps);
            _bulkUpdateSemaphore = new SemaphoreSlim(maxBulkUpdateOps, maxBulkUpdateOps);

            _logger.LogInformation(
                "ResourceThrottlingService initialized - Export: {ExportMax}, Import: {ImportMax}, BulkUpdate: {BulkUpdateMax}",
                maxExportOps,
                maxImportOps,
                maxBulkUpdateOps);
        }

        public async Task<IDisposable> AcquireAsync(
            OperationType operationType,
            CancellationToken cancellationToken = default)
        {
            var semaphore = GetSemaphore(operationType);
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Acquired throttling semaphore for {OperationType}. Available: {Available}",
                operationType,
                semaphore.CurrentCount);

            return new SemaphoreReleaseWrapper(semaphore, operationType, _logger);
        }

        public bool TryAcquire(
            OperationType operationType,
            out IDisposable semaphoreRelease)
        {
            var semaphore = GetSemaphore(operationType);
            if (semaphore.Wait(0))
            {
                semaphoreRelease = new SemaphoreReleaseWrapper(semaphore, operationType, _logger);
                _logger.LogDebug(
                    "Acquired throttling semaphore for {OperationType}. Available: {Available}",
                    operationType,
                    semaphore.CurrentCount);
                return true;
            }

            semaphoreRelease = null;
            _logger.LogDebug(
                "Failed to acquire throttling semaphore for {OperationType}. Available: {Available}",
                operationType,
                semaphore.CurrentCount);
            return false;
        }

        private SemaphoreSlim GetSemaphore(OperationType operationType)
        {
            return operationType switch
            {
                OperationType.Export => _exportSemaphore,
                OperationType.Import => _importSemaphore,
                OperationType.BulkUpdate => _bulkUpdateSemaphore,
                _ => _exportSemaphore, // Default fallback
            };
        }

        public void Dispose()
        {
            _exportSemaphore?.Dispose();
            _importSemaphore?.Dispose();
            _bulkUpdateSemaphore?.Dispose();
        }

        private sealed class SemaphoreReleaseWrapper : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private readonly OperationType _operationType;
            private readonly ILogger _logger;
            private bool _disposed = false;

            public SemaphoreReleaseWrapper(
                SemaphoreSlim semaphore,
                OperationType operationType,
                ILogger logger)
            {
                _semaphore = semaphore;
                _operationType = operationType;
                _logger = logger;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _semaphore.Release();
                    _logger.LogDebug(
                        "Released throttling semaphore for {OperationType}. Available: {Available}",
                        _operationType,
                        _semaphore.CurrentCount);
                    _disposed = true;
                }
            }
        }
    }
}
