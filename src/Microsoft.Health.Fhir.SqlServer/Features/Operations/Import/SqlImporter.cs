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
        private SqlServerFhirModel _model;
        private ISqlImportOperation _sqlImportOperation;
        private readonly ImportTaskConfiguration _importTaskConfiguration;
        private IImportErrorSerializer _importErrorSerializer;
        private ILogger<SqlImporter> _logger;

        public SqlImporter(
            ISqlImportOperation sqlImportOperation,
            SqlServerFhirModel model,
            IImportErrorSerializer importErrorSerializer,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<SqlImporter> logger)
        {
            EnsureArg.IsNotNull(sqlImportOperation, nameof(sqlImportOperation));
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(importErrorSerializer, nameof(importErrorSerializer));
            EnsureArg.IsNotNull(operationsConfig, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlImportOperation = sqlImportOperation;
            _model = model;
            _importErrorSerializer = importErrorSerializer;
            _importTaskConfiguration = operationsConfig.Value.Import;
            _logger = logger;
        }

        public async Task<ImportProcessingProgress> Import(Channel<ImportResource> inputChannel, IImportErrorStore importErrorStore, CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Start to import data to SQL data store.");
                await _model.EnsureInitialized();

                long succeededCount = 0;
                long failedCount = 0;
                long currentIndex = -1;
                var importErrorBuffer = new List<string>();
                var resourceBuffer = new List<ImportResource>();
                await foreach (var resource in inputChannel.Reader.ReadAllAsync(cancellationToken))
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

                    ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, cancellationToken, ref succeededCount, ref failedCount);
                }

                ImportResourcesInBuffer(resourceBuffer, importErrorBuffer, cancellationToken, ref succeededCount, ref failedCount);

                return await UploadImportErrorsAsync(importErrorStore, succeededCount, failedCount, importErrorBuffer.ToArray(), currentIndex, cancellationToken);
            }
            finally
            {
                _logger.LogInformation("Import data to SQL data store complete.");
            }
        }

        private void ImportResourcesInBuffer(List<ImportResource> resources, List<string> errors, CancellationToken cancellationToken, ref long succeededCount, ref long failedCount)
        {
            var resourcesWithError = resources.Where(r => !string.IsNullOrEmpty(r.ImportError));
            var resourcesWithoutError = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            var resourcesDedupped = resourcesWithoutError.GroupBy(_ => _.Resource.ToResourceKey()).Select(_ => _.First()).ToList();
            var mergedResources = _sqlImportOperation.MergeResourcesAsync(resourcesDedupped, cancellationToken).Result;
            var dupsNotMerged = resourcesWithoutError.Except(resourcesDedupped);

            errors.AddRange(resourcesWithError.Select(r => r.ImportError));
            AppendDuplicateErrorsToBuffer(dupsNotMerged, errors);

            succeededCount += mergedResources.Count();
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

        private async Task<ImportProcessingProgress> UploadImportErrorsAsync(IImportErrorStore importErrorStore, long succeededCount, long failedCount, string[] importErrors, long lastIndex, CancellationToken cancellationToken)
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
            progress.SucceedImportCount = succeededCount;
            progress.FailedImportCount = failedCount;

            return progress;
        }
    }
}
