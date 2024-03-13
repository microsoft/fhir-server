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
        private readonly IImportErrorSerializer _importErrorSerializer;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private readonly ILogger<SqlImporter> _logger;

        public SqlImporter(
            SqlServerFhirDataStore store,
            SqlServerFhirModel model,
            IImportErrorSerializer importErrorSerializer,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImporter> logger)
        {
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _importErrorSerializer = EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            _importTaskConfiguration = EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig)).Value.Import;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, ImportMode importMode, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting import to SQL data store...");

                await _model.EnsureInitialized();

                long succeededCount = 0;
                long failedCount = 0;
                long processedBytes = 0;
                long currentIndex = -1;
                var importErrorBuffer = new List<string>();
                var resourceBuffer = new List<ImportResource>();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    currentIndex = resource.Index;

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.TransactionSize)
                    {
                        continue;
                    }

                    ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, importMode, cancellationToken, ref succeededCount, ref failedCount, ref processedBytes);
                }

                ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, importMode, cancellationToken, ref succeededCount, ref failedCount, ref processedBytes);

                return await UploadImportErrorsAsync(importErrorStore, succeededCount, failedCount, importErrorBuffer.ToArray(), currentIndex, processedBytes, cancellationToken);
            }
            finally
            {
                _logger.LogInformation("Import to SQL data store completed.");
            }
        }

        private void ImportResourcesInBuffer(List<ImportResource> resources, List<string> errors, ImportMode importMode, CancellationToken cancellationToken, ref long succeededCount, ref long failedCount, ref long processedBytes)
        {
            errors.AddRange(resources.Where(r => !string.IsNullOrEmpty(r.ImportError)).Select(r => r.ImportError));
            failedCount += errors.Count;
            //// exclude resources with parsing error (ImportError != null)
            var validResources = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            var results = _store.ImportResourcesAsync(validResources, importMode, cancellationToken).Result;
            var dups = validResources.Except(results.Loaded).Except(results.Conflicts);
            AppendErrorsToBuffer(dups, results.Conflicts, errors);

            succeededCount += results.Loaded.Count;
            failedCount += dups.Count() + results.Conflicts.Count;
            processedBytes += resources.Sum(_ => (long)_.Length);

            resources.Clear();
        }

        private void AppendErrorsToBuffer(IEnumerable<ImportResource> dups, IEnumerable<ImportResource> conflicts, List<string> importErrorBuffer)
        {
            foreach (var resource in dups)
            {
                importErrorBuffer.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FailedToImportDuplicate, resource.ResourceWrapper.ResourceId, resource.Index), resource.Offset));
            }

            foreach (var resource in conflicts)
            {
                importErrorBuffer.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FaildToImportConflictingVersion, resource.ResourceWrapper.ResourceId, resource.Index), resource.Offset));
            }
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
