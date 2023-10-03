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
            var retries = 0;
            var loaded = new List<ImportResource>();
            var conflicts = new List<ImportResource>();
            while (true)
            {
                try
                {
                    loaded = new List<ImportResource>();
                    conflicts = new List<ImportResource>();
                    ImportResourcesInBufferInternal(resources, loaded, conflicts, importMode, cancellationToken).Wait();
                    break;
                }
                catch (Exception e)
                {
                    var sqlEx = (e is SqlException ? e : e.InnerException) as SqlException;
                    if (sqlEx != null && sqlEx.Number == SqlErrorCodes.Conflict && retries++ < 30)
                    {
                        _logger.LogWarning(e, $"Error on {nameof(ImportResourcesInBufferInternal)} retries={{Retries}}", retries);
                        Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    _logger.LogError(e, $"Error on {nameof(ImportResourcesInBufferInternal)} retries={{Retries}}", retries);
                    _store.StoreClient.TryLogEvent(nameof(ImportResourcesInBufferInternal), "Error", $"retries={retries} error={e}", null, cancellationToken).Wait();

                    throw;
                }
            }

            var errorResources = resources.Where(r => !string.IsNullOrEmpty(r.ImportError));
            errors.AddRange(errorResources.Select(r => r.ImportError));
            var dups = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).Except(loaded).Except(conflicts);
            AppendErrorsToBuffer(dups, conflicts, errors);

            succeededCount += loaded.Count;
            failedCount += errorResources.Count() + dups.Count() + conflicts.Count;
            processedBytes += resources.Sum(_ => (long)_.Length);

            resources.Clear();
        }

        private async Task ImportResourcesInBufferInternal(List<ImportResource> resources, List<ImportResource> loaded, List<ImportResource> conflicts, ImportMode importMode, CancellationToken cancellationToken)
        {
            var goodResources = resources.Where(r => string.IsNullOrEmpty(r.ImportError)).ToList();
            if (importMode == ImportMode.InitialLoad)
            {
                var inputDedupped = goodResources.GroupBy(_ => _.ResourceWrapper.ToResourceKey(true)).Select(_ => _.OrderBy(_ => _.ResourceWrapper.LastModified).First()).ToList();
                var current = new HashSet<ResourceKey>((await _store.GetAsync(inputDedupped.Select(_ => _.ResourceWrapper.ToResourceKey(true)).ToList(), cancellationToken)).Select(_ => _.ToResourceKey(true)));
                loaded.AddRange(inputDedupped.Where(i => !current.TryGetValue(i.ResourceWrapper.ToResourceKey(true), out _)));
                await MergeResourcesAsync(loaded, cancellationToken);
            }
            else
            {
                // If versionId and lastUpdated are specified, we assume the client may want to import multiple versions.
                var inputWithVersionLastUpdated = goodResources.Where(_ => _.KeepVersion && _.KeepLastUpdated);

                // If not, we dedup by last updated
                var inputDedupped = goodResources
                    .Where(_ => !_.KeepVersion || !_.KeepLastUpdated)
                    .GroupBy(_ => _.ResourceWrapper.ToResourceDateKey(_model.GetResourceTypeId, true))
                    .Select(_ => _.First()).ToList();

                // 2 paths:
                // 1 - if versions were specified on input then dups need to be checked within input and database.
                //   - if multiple resources with same versoin we take the latest by lastUpdated.
                var inputWithVersionDefined = inputWithVersionLastUpdated
                    .Concat(inputDedupped.Where(_ => _.KeepVersion))
                    .GroupBy(_ => _.ResourceWrapper.ToResourceKey())
                    .Select(_ => _.OrderByDescending(x => x.KeepLastUpdated ? x.ResourceWrapper.LastModified : DateTime.MinValue).First())
                    .ToList();

                var currentKeys = new HashSet<ResourceKey>((await _store.GetAsync(inputWithVersionDefined.Select(_ => _.ResourceWrapper.ToResourceKey()).ToList(), cancellationToken)).Select(_ => _.ToResourceKey()));
                loaded.AddRange(inputWithVersionDefined.Where(i => !currentKeys.TryGetValue(i.ResourceWrapper.ToResourceKey(), out _)).OrderBy(_ => _.ResourceWrapper.ResourceId).ThenByDescending(_ => _.ResourceWrapper.LastModified)); // sorting is used in merge to set isHistory
                await MergeResourcesAsync(loaded, cancellationToken);

                // 2 - if versions were not specified they have to be assigned as next based on union of input and database.
                // assume that only one unassigned version is provided for a given resource as we cannot guarantee processing order across parallel file streams anyway
                var inputDeduppedNoVersion = inputDedupped.Where(_ => !_.KeepVersion).GroupBy(_ => _.ResourceWrapper.ToResourceKey(true)).Select(_ => _.First()).ToList();
                //// check whether record can fit
                var currentDates = (await _store.GetAsync(inputDeduppedNoVersion.Select(_ => _.ResourceWrapper.ToResourceKey(true)).ToList(), cancellationToken)).ToDictionary(_ => _.ToResourceKey(true), _ => _.ToResourceDateKey(_model.GetResourceTypeId));
                var inputDeduppedNoVersionForCheck = new List<ImportResource>();
                foreach (var resource in inputDeduppedNoVersion)
                {
                    if (currentDates.TryGetValue(resource.ResourceWrapper.ToResourceKey(true), out var dateKey)
                        && ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.ResourceWrapper.LastModified.DateTime) < dateKey.ResourceSurrogateId)
                    {
                        inputDeduppedNoVersionForCheck.Add(resource);
                    }
                }

                var versionSlots = (await _store.StoreClient.GetResourceVersionsAsync(inputDeduppedNoVersionForCheck.Select(_ => _.ResourceWrapper.ToResourceDateKey(_model.GetResourceTypeId)).ToList(), cancellationToken)).ToDictionary(_ => new ResourceKey(_model.GetResourceTypeName(_.ResourceTypeId), _.Id, null), _ => _);
                foreach (var resource in inputDeduppedNoVersionForCheck)
                {
                    var resourceKey = resource.ResourceWrapper.ToResourceKey(true);
                    versionSlots.TryGetValue(resourceKey, out var versionSlotKey);

                    if (versionSlotKey.VersionId == "0") // no version slot available
                    {
                        conflicts.Add(resource);
                    }
                    else
                    {
                        resource.KeepVersion = true;
                        resource.ResourceWrapper.Version = versionSlotKey.VersionId;
                    }
                }

                var inputDeduppedNoVersionNoConflict = inputDeduppedNoVersion.Except(conflicts).ToList(); // some resources might get version assigned
                await MergeResourcesAsync(inputDeduppedNoVersionNoConflict.Where(_ => _.KeepVersion).ToList(), cancellationToken);
                await MergeResourcesAsync(inputDeduppedNoVersionNoConflict.Where(_ => !_.KeepVersion).ToList(), cancellationToken);
                loaded.AddRange(inputDeduppedNoVersionNoConflict);
            }
        }

        private async Task MergeResourcesAsync(IList<ImportResource> resources, CancellationToken cancellationToken)
        {
            var input = resources.Where(_ => _.KeepLastUpdated).Select(_ => new ResourceWrapperOperation(_.ResourceWrapper, true, true, null, requireETagOnUpdate: false, keepVersion: _.KeepVersion, bundleResourceContext: null)).ToList();
            await _store.MergeInternalAsync(input, true, false, cancellationToken);
            input = resources.Where(_ => !_.KeepLastUpdated).Select(_ => new ResourceWrapperOperation(_.ResourceWrapper, true, true, null, requireETagOnUpdate: false, keepVersion: _.KeepVersion, bundleResourceContext: null)).ToList();
            await _store.MergeInternalAsync(input, false, false, cancellationToken);
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
