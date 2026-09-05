// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlImporter : IImporter
    {
        private readonly SqlServerFhirDataStore _store;
        private readonly SqlServerFhirModel _model;
        private readonly ImportJobConfiguration _importTaskConfiguration;
        private readonly ILogger<SqlImporter> _logger;

        public SqlImporter(
            SqlServerFhirDataStore store,
            SqlServerFhirModel model,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImporter> logger)
        {
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _importTaskConfiguration = EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig)).Value.Import;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, ImportMode importMode, bool allowNegativeVersions, bool eventualConsistency, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting import to SQL data store...");

                await _model.EnsureInitialized();

                long succeededCount = 0;
                long processedBytes = 0;
                long getResourcesMilliseconds = 0;
                long mergeResourcesMilliseconds = 0;
                long getResourcesCallCount = 0;
                long mergeResourcesCallCount = 0;
                long currentIndex = -1;
                var errors = new List<string>();
                var resourceBatch = new List<ImportResource>();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    currentIndex = resource.Index;

                    resourceBatch.Add(resource);
                    if (resourceBatch.Count < _importTaskConfiguration.TransactionSize)
                    {
                        continue;
                    }

                    var resultInt = await ImportResourcesInBuffer(resourceBatch, errors, importMode, allowNegativeVersions, eventualConsistency, cancellationToken);
                    succeededCount += resultInt.LoadedCount;
                    processedBytes += resultInt.ProcessedBytes;
                    getResourcesMilliseconds += resultInt.GetResourcesMilliseconds;
                    mergeResourcesMilliseconds += resultInt.MergeResourcesMilliseconds;
                    getResourcesCallCount += resultInt.GetResourcesCallCount;
                    mergeResourcesCallCount += resultInt.MergeResourcesCallCount;
                }

                var result = await ImportResourcesInBuffer(resourceBatch, errors, importMode, allowNegativeVersions, eventualConsistency, cancellationToken);
                succeededCount += result.LoadedCount;
                processedBytes += result.ProcessedBytes;
                getResourcesMilliseconds += result.GetResourcesMilliseconds;
                mergeResourcesMilliseconds += result.MergeResourcesMilliseconds;
                getResourcesCallCount += result.GetResourcesCallCount;
                mergeResourcesCallCount += result.MergeResourcesCallCount;

                return await UploadImportErrorsAsync(importErrorStore, succeededCount, errors.Count, errors.ToArray(), currentIndex, processedBytes, getResourcesMilliseconds, mergeResourcesMilliseconds, getResourcesCallCount, mergeResourcesCallCount, cancellationToken);
            }
            finally
            {
                _logger.LogInformation("Import to SQL data store completed.");
            }
        }

        private async Task<(long LoadedCount, long ProcessedBytes, long GetResourcesMilliseconds, long MergeResourcesMilliseconds, long GetResourcesCallCount, long MergeResourcesCallCount)> ImportResourcesInBuffer(List<ImportResource> resources, List<string> errors, ImportMode importMode, bool allowNegativeVersions, bool eventualConsistency, CancellationToken cancellationToken)
        {
            errors.AddRange(resources.Where(r => !string.IsNullOrEmpty(r.ImportError)).Select(r => r.ImportError));
            //// exclude resources with parsing error (ImportError != null)
            var validResources = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            var importResult = await _store.ImportResourcesAsync(validResources, importMode, allowNegativeVersions, eventualConsistency, cancellationToken);
            errors.AddRange(importResult.Errors);
            var totalBytes = resources.Sum(_ => (long)_.Length);
            resources.Clear();
            return (validResources.Count - importResult.Errors.Count, totalBytes, importResult.GetResourcesMilliseconds, importResult.MergeResourcesMilliseconds, importResult.GetResourcesCallCount, importResult.MergeResourcesCallCount);
        }

        private async Task<ImportProcessingProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeededCount, long failedCount, string[] importErrors, long lastIndex, long processedBytes, long getResourcesMilliseconds, long mergeResourcesMilliseconds, long getResourcesCallCount, long mergeResourcesCallCount, CancellationToken cancellationToken)
        {
            try
            {
                await importErrorStore.UploadErrorsAsync(importErrors, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload error logs.");
                throw;
            }

            var progress = new ImportProcessingProgress();
            progress.SucceededResources = succeededCount;
            progress.FailedResources = failedCount;
            progress.ProcessedBytes = processedBytes;
            progress.CurrentIndex = lastIndex + 1;
            progress.GetResourcesMilliseconds = getResourcesMilliseconds;
            progress.MergeResourcesMilliseconds = mergeResourcesMilliseconds;
            progress.GetResourcesCallCount = getResourcesCallCount;
            progress.MergeResourcesCallCount = mergeResourcesCallCount;

            return progress;
        }
    }
}
