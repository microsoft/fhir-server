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

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    internal class SqlImporter : IImporter
    {
        private ISqlBulkCopyDataWrapperFactory _sqlBulkCopyDataWrapperFactory;
        private ISqlImportOperation _sqlImportOperation;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private IImportErrorSerializer _importErrorSerializer;
        private ILogger<SqlImporter> _logger;

        public SqlImporter(
            ISqlImportOperation sqlImportOperation,
            ISqlBulkCopyDataWrapperFactory sqlBulkCopyDataWrapperFactory,
            IImportErrorSerializer importErrorSerializer,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImporter> logger)
        {
            EnsureArg.IsNotNull(sqlImportOperation, nameof(sqlImportOperation));
            EnsureArg.IsNotNull(sqlBulkCopyDataWrapperFactory, nameof(sqlBulkCopyDataWrapperFactory));
            EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlImportOperation = sqlImportOperation;
            _sqlBulkCopyDataWrapperFactory = sqlBulkCopyDataWrapperFactory;
            _importErrorSerializer = importErrorSerializer;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _logger = logger;
        }

        public async Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Starting import to SQL data store...");

                long succeedCount = 0;
                long failedCount = 0;
                long currentIndex = -1;
                var importErrorBuffer = new List<string>();
                var resourceBuffer = new List<ImportResource>();

                await _sqlBulkCopyDataWrapperFactory.EnsureInitializedAsync();
                await foreach (ImportResource resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }

                    currentIndex = resource.Index;

                    resourceBuffer.Add(resource);
                    if (resourceBuffer.Count < _importTaskConfiguration.SqlBatchSizeForImportResourceOperation)
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
            var resourcesWithError = resources.Where(r => r.ContainsError());
            var resourcesWithoutError = resources.Where(r => !r.ContainsError()).ToList();
            var resourcesDedupped = resourcesWithoutError.GroupBy(_ => _.Resource.ToResourceKey()).Select(_ => _.First()).ToList();
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
                importErrorBuffer.Add(_importErrorSerializer.Serialize(resource.Index, string.Format(Resources.FailedToImportForDuplicatedResource, resource.Resource.ResourceId, resource.Index), resource.Offset));
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
            progress.SucceedImportCount = succeedCount;
            progress.FailedImportCount = failedCount;
            progress.CurrentIndex = lastIndex + 1;

            // Return progress for checkpoint progress
            return progress;
        }
    }
}
