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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlImporter : IImporter
    {
        private readonly SqlServerFhirModel _model;
        private readonly ISqlImportOperation _sqlImportOperation;
        private readonly ImportJobConfiguration _importTaskConfiguration;
        private readonly IImportErrorSerializer _importErrorSerializer;
        private readonly ILogger<SqlImporter> _logger;

        public SqlImporter(
            ISqlImportOperation sqlImportOperation,
            SqlServerFhirModel model,
            IImportErrorSerializer importErrorSerializer,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImporter> logger)
        {
            _sqlImportOperation = EnsureArg.IsNotNull(sqlImportOperation, nameof(sqlImportOperation));
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _importErrorSerializer = EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            _importTaskConfiguration = EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig)).Value.Import;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting import to SQL data store...");

                await _model.EnsureInitialized();

                long succeedCount = 0;
                long failedCount = 0;
                long currentIndex = -1;
                var importErrorBuffer = new List<string>();
                var resourceBuffer = new List<ImportResource>();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    currentIndex = resource.Index;

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.TransactionSize)
                    {
                        continue;
                    }

                    ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, cancellationToken, ref succeedCount, ref failedCount);
                }

                ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, cancellationToken, ref succeedCount, ref failedCount);

                return await UploadImportErrorsAsync(importErrorStore, succeedCount, failedCount, importErrorBuffer.ToArray(), currentIndex, cancellationToken);
            }
            finally
            {
                _logger.LogInformation("Import to SQL data store completed.");
            }
        }

        private void ImportResourcesInBuffer(List<ImportResource> resources, List<string> errors, CancellationToken cancellationToken, ref long succeedCount, ref long failedCount)
        {
            var resourcesWithError = resources.Where(r => !string.IsNullOrEmpty(r.ImportError));
            var resourcesWithoutError = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            var resourcesDedupped = resourcesWithoutError.GroupBy(_ => _.ResourceWrapper.ToResourceKey()).Select(_ => _.First()).ToList();
            var mergedResources = _sqlImportOperation.MergeResourcesAsync(resourcesDedupped, cancellationToken).Result;
            var dupsNotMerged = resourcesWithoutError.Except(resourcesDedupped);

            errors.AddRange(resourcesWithError.Select(r => r.ImportError));
            AppendDuplicateErrorsToBuffer(dupsNotMerged, errors);

            succeedCount += mergedResources.Count();
            failedCount += resourcesWithError.Count() + dupsNotMerged.Count();

            resources.Clear();
        }

        private void AppendDuplicateErrorsToBuffer(IEnumerable<ImportResource> resources, List<string> importErrorBuffer)
        {
            foreach (var resource in resources)
            {
                importErrorBuffer.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FailedToImportForDuplicatedResource, resource.ResourceWrapper.ResourceId, resource.Index), resource.Offset));
            }
        }

        private async Task<ImportProcessingProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeedCount, long failedCount, string[] importErrors, long lastIndex, CancellationToken cancellationToken)
        {
            try
            {
                await importErrorStore.UploadErrorsAsync(importErrors, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Failed to upload error logs.");
                throw;
            }

            var progress = new ImportProcessingProgress();
            progress.SucceededResources = succeedCount;
            progress.FailedResources = failedCount;
            progress.CurrentIndex = lastIndex + 1;

            // Return progress for checkpoint progress
            return progress;
        }
    }
}
