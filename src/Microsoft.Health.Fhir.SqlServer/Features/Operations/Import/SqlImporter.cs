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
                }

                var result = await ImportResourcesInBuffer(resourceBatch, errors, importMode, allowNegativeVersions, eventualConsistency, cancellationToken);
                succeededCount += result.LoadedCount;
                processedBytes += result.ProcessedBytes;

                return await UploadImportErrorsAsync(importErrorStore, succeededCount, errors.Count, errors.ToArray(), currentIndex, processedBytes, cancellationToken);
            }
            finally
            {
                _logger.LogInformation("Import to SQL data store completed.");
            }
        }

        private async Task<(long LoadedCount, long ProcessedBytes)> ImportResourcesInBuffer(List<ImportResource> resources, List<string> errors, ImportMode importMode, bool allowNegativeVersions, bool eventualConsistency, CancellationToken cancellationToken)
        {
            errors.AddRange(resources.Where(r => !string.IsNullOrEmpty(r.ImportError)).Select(r => r.ImportError));
            //// exclude resources with parsing error (ImportError != null)
            var validResources = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            var newErrors = await _store.ImportResourcesAsync(validResources, importMode, allowNegativeVersions, eventualConsistency, cancellationToken);
            errors.AddRange(newErrors);
            var totalBytes = resources.Sum(_ => (long)_.Length);
            resources.Clear();
            return (validResources.Count - newErrors.Count, totalBytes);
        }

        private async Task<ImportProcessingProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeededCount, long failedCount, string[] importErrors, long lastIndex, long processedBytes, CancellationToken cancellationToken)
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

            return progress;
        }
    }
}
