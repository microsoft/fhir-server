// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.IO;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// A SQL Server-backed <see cref="IFhirDataStore"/>.
    /// </summary>
    internal class SqlServerFhirDataStore : IFhirDataStore, IProvideCapability
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISqlServerFhirModel _model;
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _upsertResourceTvpGeneratorVLatest;
        private readonly VLatest.MergeResourcesTvpGenerator<IReadOnlyList<MergeResourceWrapper>> _mergeResourcesTvpGeneratorVLatest;
        private readonly VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> _reindexResourceTvpGeneratorVLatest;
        private readonly VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> _bulkReindexResourcesTvpGeneratorVLatest;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;
        private readonly CoreFeatureConfiguration _coreFeatures;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ICompressedRawResourceConverter _compressedRawResourceConverter;
        private readonly ILogger<SqlServerFhirDataStore> _logger;
        private readonly SchemaInformation _schemaInformation;
        private readonly IModelInfoProvider _modelInfoProvider;
        private const string InitialVersion = "1";
        public const string MergeResourcesDisabledFlagId = "MergeResources.IsDisabled";
        private static MergeResourcesRetriesFlag _mergeResourcesRetriesFlag;
        private static object _mergeResourcesFlagLocker = new object();

        public SqlServerFhirDataStore(
            ISqlServerFhirModel model,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap,
            VLatest.UpsertResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> upsertResourceTvpGeneratorVLatest,
            VLatest.MergeResourcesTvpGenerator<IReadOnlyList<MergeResourceWrapper>> mergeResourcesTvpGeneratorVLatest,
            VLatest.ReindexResourceTvpGenerator<IReadOnlyList<ResourceWrapper>> reindexResourceTvpGeneratorVLatest,
            VLatest.BulkReindexResourcesTvpGenerator<IReadOnlyList<ResourceWrapper>> bulkReindexResourcesTvpGeneratorVLatest,
            IOptions<CoreFeatureConfiguration> coreFeatures,
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ICompressedRawResourceConverter compressedRawResourceConverter,
            ILogger<SqlServerFhirDataStore> logger,
            SchemaInformation schemaInformation,
            IModelInfoProvider modelInfoProvider,
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _model = EnsureArg.IsNotNull(model, nameof(model));
            _searchParameterTypeMap = EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));
            _upsertResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(upsertResourceTvpGeneratorVLatest, nameof(upsertResourceTvpGeneratorVLatest));
            _mergeResourcesTvpGeneratorVLatest = EnsureArg.IsNotNull(mergeResourcesTvpGeneratorVLatest, nameof(mergeResourcesTvpGeneratorVLatest));
            _reindexResourceTvpGeneratorVLatest = EnsureArg.IsNotNull(reindexResourceTvpGeneratorVLatest, nameof(reindexResourceTvpGeneratorVLatest));
            _bulkReindexResourcesTvpGeneratorVLatest = EnsureArg.IsNotNull(bulkReindexResourcesTvpGeneratorVLatest, nameof(bulkReindexResourcesTvpGeneratorVLatest));
            _coreFeatures = EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));
            _sqlConnectionWrapperFactory = EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            _compressedRawResourceConverter = EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _schemaInformation = EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));

            _memoryStreamManager = new RecyclableMemoryStreamManager();

            if (_mergeResourcesRetriesFlag == null)
            {
                lock (_mergeResourcesFlagLocker)
                {
                    _mergeResourcesRetriesFlag ??= new MergeResourcesRetriesFlag(_sqlConnectionWrapperFactory);
                }
            }
        }

        public async Task<IDictionary<ResourceKey, UpsertOutcome>> MergeAsync(IReadOnlyList<ResourceWrapperOperation> resources, CancellationToken cancellationToken)
        {
            var retries = 0;
            while (true)
            {
                var results = new Dictionary<ResourceKey, UpsertOutcome>();
                if (resources == null || resources.Count == 0)
                {
                    return results;
                }

                cancellationToken.ThrowIfCancellationRequested();

                var mergeStart = (DateTime?)null;
                try
                {
                    // ignore input resource version to get latest version from the store
                    var existingResources = (await GetAsync(resources.Select(r => r.Wrapper.ToResourceKey(true)).Distinct().ToList(), cancellationToken)).ToDictionary(r => r.ToResourceKey(true), r => r);

                    // assume that most likely case is that all resources should be updated
                    (var minSurrId, var minSequenceId) = await MergeResourcesBeginTransactionAsync(resources.Count, cancellationToken);

                    var index = 0;
                    var mergeWrappers = new List<MergeResourceWrapper>();
                    foreach (var resourceExt in resources)
                    {
                        var weakETag = resourceExt.WeakETag;
                        int? eTag = weakETag == null
                            ? null
                            : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

                        var resource = resourceExt.Wrapper;
                        var resourceKey = resource.ToResourceKey(); // keep input version in the results to allow processing multiple versions per resource
                        existingResources.TryGetValue(resource.ToResourceKey(true), out var existingResource);

                        // Check for any validation errors
                        if (existingResource != null && eTag.HasValue && !string.Equals(eTag.ToString(), existingResource.Version, StringComparison.Ordinal))
                        {
                            if (weakETag != null)
                            {
                                // The backwards compatibility behavior of Stu3 is to return 409 Conflict instead of a 412 Precondition Failed
                                if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                                {
                                    throw new ResourceConflictException(weakETag);
                                }

                                throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag.VersionId));
                            }
                        }

                        // There is no previous version of this resource, check validations and then simply call SP to create new version
                        if (existingResource == null)
                        {
                            if (resource.IsDeleted)
                            {
                                // Don't bother marking the resource as deleted since it already does not exist.
                                results.Add(resourceKey, null);
                                continue;
                            }

                            if (eTag.HasValue)
                            {
                                // You can't update a resource with a specified version if the resource does not exist
                                if (weakETag != null)
                                {
                                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, resource.ResourceTypeName, resource.ResourceId, weakETag.VersionId));
                                }
                            }

                            if (!resourceExt.AllowCreate)
                            {
                                throw new MethodNotAllowedException(Core.Resources.ResourceCreationNotAllowed);
                            }

                            resource.Version = InitialVersion;
                        }
                        else
                        {
                            if (resourceExt.RequireETagOnUpdate && !eTag.HasValue)
                            {
                                // This is a versioned update and no version was specified
                                // TODO: Add this to SQL error codes in AB#88286
                                // The backwards compatibility behavior of Stu3 is to return 412 Precondition Failed instead of a 400 Bad Request
                                if (_modelInfoProvider.Version == FhirSpecification.Stu3)
                                {
                                    throw new PreconditionFailedException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName));
                                }

                                throw new BadRequestException(string.Format(Core.Resources.IfMatchHeaderRequiredForResource, resource.ResourceTypeName));
                            }

                            if (resource.IsDeleted && existingResource.IsDeleted)
                            {
                                // Already deleted - don't create a new version
                                results.Add(resourceKey, null);
                                continue;
                            }

                            // check if resources are equal if its not a Delete action
                            if (!resource.IsDeleted)
                            {
                                // check if the new resource data is same as existing resource data
                                if (ExistingRawResourceIsEqualToInput(resource, existingResource))
                                {
                                    // Send the existing resource in the response
                                    results.Add(resourceKey, new UpsertOutcome(existingResource, SaveOutcomeType.Updated));
                                    continue;
                                }
                            }

                            // existing version in the SQL db should never be null
                            resource.Version = (int.Parse(existingResource.Version) + 1).ToString(CultureInfo.InvariantCulture);
                        }

                        var surrIdBase = ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(resource.LastModified.DateTime);
                        var surrId = surrIdBase + minSequenceId + index;
                        ReplaceVersionIdInMeta(resource);
                        mergeWrappers.Add(new MergeResourceWrapper(resource, surrId, resourceExt.KeepHistory, true)); // TODO: When multiple versions for a resource are supported use correct value instead of last true.
                        index++;
                        results.Add(resourceKey, new UpsertOutcome(resource, resource.Version == InitialVersion ? SaveOutcomeType.Created : SaveOutcomeType.Updated));
                    }

                    if (mergeWrappers.Count > 0) // do not call db with empty input
                    {
                        mergeStart = DateTime.UtcNow;
                        using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true); // TODO: Remove tran enlist when true bundle logic is in place.
                        using var cmd = conn.CreateNonRetrySqlCommand();
                        VLatest.MergeResources.PopulateCommand(
                            cmd,
                            AffectedRows: 0,
                            RaiseExceptionOnConflict: true,
                            IsResourceChangeCaptureEnabled: _coreFeatures.SupportsResourceChangeCapture,
                            tableValuedParameters: _mergeResourcesTvpGeneratorVLatest.Generate(mergeWrappers));
                        cmd.CommandTimeout = 180 + (int)(3600.0 / 10000 * mergeWrappers.Count);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await MergeResourcesCommitTransactionAsync(minSurrId, cancellationToken);

                    return results;
                }
                catch (SqlException e)
                {
                    var isExecutionTimeout = false;
                    var isConflict = false;
                    if (((isConflict = e.Number == SqlErrorCodes.Conflict) && retries++ < 30) // retries on conflict should never be more than 1, so it is OK to hardcode.
                        //// we cannot retry today on connection loss as this call might be in outer transaction, hence _mergeResourcesRetriesFlag
                        //// TODO: remove _mergeResourcesRetriesFlag when set bundle processing is in place.
                        || (_mergeResourcesRetriesFlag.IsEnabled() && e.IsRetriable()) // this should allow to deal with intermittent database errors.
                        || ((isExecutionTimeout = _mergeResourcesRetriesFlag.IsEnabled() && e.IsExecutionTimeout()) && retries++ < 3)) // timeouts happen once in a while on highly loaded databases.
                    {
                        _logger.LogWarning(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                        if (isConflict || isExecutionTimeout)
                        {
                            await TryLogEvent(nameof(MergeAsync), "Warn", $"Error={e.Message}, retries={retries}", mergeStart, cancellationToken);
                        }

                        await Task.Delay(5000, cancellationToken);
                        continue;
                    }

                    _logger.LogError(e, $"Error from SQL database on {nameof(MergeAsync)} retries={{Retries}}", retries);
                    await TryLogEvent(nameof(MergeAsync), "Error", $"Error={e.Message}, retries={retries}", mergeStart, cancellationToken);
                    throw;
                }
            }
        }

        private async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            try
            {
                using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
                using var cmd = conn.CreateNonRetrySqlCommand();
                VLatest.LogEvent.PopulateCommand(cmd, process, status, null, null, null, null, startDate, text, null, null);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // do nothing;
            }
        }

        public async Task<UpsertOutcome> UpsertAsync(ResourceWrapperOperation resource, CancellationToken cancellationToken)
        {
            return (await MergeAsync(new List<ResourceWrapperOperation> { resource }, cancellationToken)).First().Value;
        }

        public async Task<IReadOnlyList<ResourceWrapper>> GetAsync(IReadOnlyList<ResourceKey> keys, CancellationToken cancellationToken)
        {
            var results = new List<ResourceWrapper>();
            if (keys == null || keys.Count == 0)
            {
                return results;
            }

            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateRetrySqlCommand();
            VLatest.GetResources.PopulateCommand(cmd, keys.Select(_ => new ResourceKeyListRow(_model.GetResourceTypeId(_.ResourceType), _.Id, _.VersionId == null ? null : int.TryParse(_.VersionId, out var version) ? version : int.MinValue))); // put min value when cannot parse so resource will be not found
            cmd.CommandTimeout = 180 + (int)(1200.0 / 10000 * keys.Count);

            using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
            var resources = new List<ResourceWrapper>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = VLatest.Resource;
                var resourceTypeId = reader.Read(table.ResourceTypeId, 0);
                var resourceId = reader.Read(table.ResourceId, 1);
                var resourceSurrogateId = reader.Read(table.ResourceSurrogateId, 2);
                var version = reader.Read(table.Version, 3);
                var isDeleted = reader.Read(table.IsDeleted, 4);
                var isHistory = reader.Read(table.IsHistory, 5);
                var rawResourceBytes = reader.GetSqlBytes(6).Value;
                var isRawResourceMetaSet = reader.Read(table.IsRawResourceMetaSet, 7);
                var searchParamHash = reader.Read(table.SearchParamHash, 8);

                using var rawResourceStream = new MemoryStream(rawResourceBytes);
                var rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

                var resource = new ResourceWrapper(
                    resourceId,
                    version.ToString(CultureInfo.InvariantCulture),
                    _model.GetResourceTypeName(resourceTypeId),
                    new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                    null,
                    new DateTimeOffset(ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated(resourceSurrogateId), TimeSpan.Zero),
                    isDeleted,
                    searchIndices: null,
                    compartmentIndices: null,
                    lastModifiedClaims: null,
                    searchParameterHash: searchParamHash)
                {
                    IsHistory = isHistory,
                };

                resources.Add(resource);
            }

            await reader.NextResultAsync(cancellationToken);

            return resources;
        }

        public async Task<ResourceWrapper> GetAsync(ResourceKey key, CancellationToken cancellationToken)
        {
            var results = await GetAsync(new List<ResourceKey> { key }, cancellationToken);
            return results.Count == 0 ? null : results[0];
        }

        public async Task HardDeleteAsync(ResourceKey key, bool keepCurrentVersion, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.HardDeleteResource.PopulateCommand(sqlCommandWrapper, resourceTypeId: _model.GetResourceTypeId(key.ResourceType), resourceId: key.Id, Convert.ToInt16(keepCurrentVersion));
                await sqlCommandWrapper.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        public async Task BulkUpdateSearchParameterIndicesAsync(IReadOnlyCollection<ResourceWrapper> resources, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.BulkReindexResources.PopulateCommand(
                    sqlCommandWrapper,
                    _bulkReindexResourcesTvpGeneratorVLatest.Generate(resources.ToList()));

                // We will reindex the rest of the batch if one resource has a versioning conflict
                int? failedResourceCount;
                try
                {
                    failedResourceCount = (int?)await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
                }
                catch (SqlException e)
                {
                    _logger.LogError(e, "Error from SQL database on reindex.");
                    throw;
                }

                if (failedResourceCount != 0)
                {
                    string message = string.Format(Core.Resources.ReindexingResourceVersionConflictWithCount, failedResourceCount);
                    string userAction = Core.Resources.ReindexingUserAction;
                    _logger.LogError("{Error}", message);
                    throw new PreconditionFailedException(message + " " + userAction);
                }
            }
        }

        private static string RemoveTrailingZerosFromMillisecondsForAGivenDate(DateTimeOffset date)
        {
            // 0000000+ -> +, 0010000+ -> 001+, 0100000+ -> 01+, 0180000+ -> 018+, 1000000 -> 1+, 1100000+ -> 11+, 1010000+ -> 101+
            // ToString("o") - Formats to 2022-03-09T01:40:52.0690000+02:00 but serialized value to string in dB is 2022-03-09T01:40:52.069+02:00
            var formattedDate = date.ToString("o", CultureInfo.InvariantCulture);
            var milliseconds = formattedDate.Substring(20, 7); // get 0690000
            var trimmedMilliseconds = milliseconds.TrimEnd('0'); // get 069
            if (milliseconds.Equals("0000000", StringComparison.Ordinal))
            {
                // when date = 2022-03-09T01:40:52.0000000+02:00, value in dB is 2022-03-09T01:40:52+02:00, we need to replace the . after second
                return formattedDate.Replace("." + milliseconds, string.Empty, StringComparison.Ordinal);
            }

            return formattedDate.Replace(milliseconds, trimmedMilliseconds, StringComparison.Ordinal);
        }

        // TODO: Remove when Merge is min supported version
        private static string RemoveVersionIdAndLastUpdatedFromMeta(ResourceWrapper resourceWrapper)
        {
            var versionToReplace = resourceWrapper.RawResource.IsMetaSet ? resourceWrapper.Version : "1";
            var rawResource = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{versionToReplace}\"", string.Empty, StringComparison.Ordinal);
            return rawResource.Replace($"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", string.Empty, StringComparison.Ordinal);
        }

        private void ReplaceVersionIdInMeta(ResourceWrapper resourceWrapper)
        {
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                return;
            }

            var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);
            var rawResourceData = resourceWrapper.RawResource.Data.Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal);
            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private void ReplaceVersionIdAndLastUpdatedInMeta(ResourceWrapper resourceWrapper)
        {
            var date = GetJsonValue(resourceWrapper.RawResource.Data, "lastUpdated", false);
            string rawResourceData;
            if (resourceWrapper.Version == InitialVersion) // version is already correct
            {
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }
            else
            {
                var version = GetJsonValue(resourceWrapper.RawResource.Data, "versionId", false);
                rawResourceData = resourceWrapper.RawResource.Data
                                    .Replace($"\"versionId\":\"{version}\"", $"\"versionId\":\"{resourceWrapper.Version}\"", StringComparison.Ordinal)
                                    .Replace($"\"lastUpdated\":\"{date}\"", $"\"lastUpdated\":\"{RemoveTrailingZerosFromMillisecondsForAGivenDate(resourceWrapper.LastModified)}\"", StringComparison.Ordinal);
            }

            resourceWrapper.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private bool ExistingRawResourceIsEqualToInput(ResourceWrapper input, ResourceWrapper existing) // call is not symmetrical, it assumes version = 1 on input.
        {
            var inputDate = GetJsonValue(input.RawResource.Data, "lastUpdated", false);
            var existingDate = GetJsonValue(existing.RawResource.Data, "lastUpdated", true);
            var existingVersion = GetJsonValue(existing.RawResource.Data, "versionId", true);
            if (existingVersion != InitialVersion)
            {
                return input.RawResource.Data == existing.RawResource.Data.Replace($"\"lastUpdated\":\"{existingDate}\"", $"\"lastUpdated\":\"{inputDate}\"", StringComparison.Ordinal);
            }
            else
            {
                return input.RawResource.Data
                            == existing.RawResource.Data
                                    .Replace($"\"versionId\":\"{existingVersion}\"", $"\"versionId\":\"{InitialVersion}\"", StringComparison.Ordinal)
                                    .Replace($"\"lastUpdated\":\"{existingDate}\"", $"\"lastUpdated\":\"{inputDate}\"", StringComparison.Ordinal);
            }
        }

        // This method relies on current raw resource string formatting, i.e. no extra spaces.
        // This logic should be removed once "resource.meta not available" bug is fixed.
        private string GetJsonValue(string json, string propName, bool isExisting)
        {
            var startIndex = json.IndexOf($"\"{propName}\":\"", StringComparison.Ordinal);
            if (startIndex == -1)
            {
                _logger.LogError($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")} {json}");
                return string.Empty;
            }

            startIndex = startIndex + propName.Length + 4;
            var endIndex = json.IndexOf("\"", startIndex, StringComparison.Ordinal);
            if (endIndex == -1)
            {
                _logger.LogError($"Cannot parse {propName} value from {(isExisting ? "existing" : "input")} {json}");
                return string.Empty;
            }

            var value = json.Substring(startIndex, endIndex - startIndex);

            return value;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            builder.PopulateDefaultResourceInteractions()
                .SyncSearchParameters()
                .AddGlobalSearchParameters()
                .SyncProfiles();

            if (_coreFeatures.SupportsBatch)
            {
                // Batch supported added in listedCapability
                builder.AddGlobalInteraction(SystemRestfulInteraction.Batch);
            }

            if (_coreFeatures.SupportsTransaction)
            {
                // Transaction supported added in listedCapability
                builder.AddGlobalInteraction(SystemRestfulInteraction.Transaction);
            }
        }

        internal async Task<(long SurrId, int Sequence)> MergeResourcesBeginTransactionAsync(int resourceVersionCount, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateNonRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeResourcesBeginTransaction";
            cmd.Parameters.AddWithValue("@Count", resourceVersionCount);
            var surrogateIdParam = new SqlParameter("@SurrogateIdRangeFirstValue", SqlDbType.BigInt) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(surrogateIdParam);
            var sequenceParam = new SqlParameter("@SequenceRangeFirstValue", SqlDbType.Int) { Direction = ParameterDirection.Output };
            cmd.Parameters.Add(sequenceParam);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            return ((long)surrogateIdParam.Value, (int)sequenceParam.Value);
        }

        internal async Task MergeResourcesCommitTransactionAsync(long surrogateIdRangeFirstValue, CancellationToken cancellationToken)
        {
            using var conn = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, false);
            using var cmd = conn.CreateNonRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeResourcesCommitTransaction";
            cmd.Parameters.AddWithValue("@SurrogateIdRangeFirstValue", surrogateIdRangeFirstValue);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<ResourceWrapper> UpdateSearchParameterIndicesAsync(ResourceWrapper resource, WeakETag weakETag, CancellationToken cancellationToken)
        {
            int? eTag = weakETag == null
                ? null
                : (int.TryParse(weakETag.VersionId, out var parsedETag) ? parsedETag : -1); // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.ReindexResource.PopulateCommand(
                    sqlCommandWrapper,
                    resourceTypeId: _model.GetResourceTypeId(resource.ResourceTypeName),
                    resourceId: resource.ResourceId,
                    eTag,
                    searchParamHash: resource.SearchParameterHash,
                    tableValuedParameters: _reindexResourceTvpGeneratorVLatest.Generate(new List<ResourceWrapper> { resource }));

                try
                {
                    await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);

                    return resource;
                }
                catch (SqlException e)
                {
                    switch (e.Number)
                    {
                        case SqlErrorCodes.PreconditionFailed:
                            _logger.LogError(string.Format(Core.Resources.ResourceVersionConflict, weakETag));
                            throw new PreconditionFailedException(string.Format(Core.Resources.ResourceVersionConflict, weakETag));

                        default:
                            _logger.LogError(e, "Error from SQL database on reindex.");
                            throw;
                    }
                }
            }
        }

        public async Task<int?> GetProvisionedDataStoreCapacityAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult((int?)null);
        }

        private class MergeResourcesRetriesFlag
        {
            private const string FlagId = "MergeResources.RetriesOnRetriableErrors.IsEnabled";
            private SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
            private bool _isEnabled;
            private DateTime? _lastUpdated;
            private object _databaseAccessLocker = new object();

            public MergeResourcesRetriesFlag(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
            {
                _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            }

            public bool IsEnabled()
            {
                if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
                {
                    return _isEnabled;
                }

                lock (_databaseAccessLocker)
                {
                    if (_lastUpdated.HasValue && (DateTime.UtcNow - _lastUpdated.Value).TotalSeconds < 600)
                    {
                        return _isEnabled;
                    }

                    _isEnabled = IsEnabledInDatabase();
                    _lastUpdated = DateTime.UtcNow;
                }

                return _isEnabled;
            }

            private bool IsEnabledInDatabase()
            {
                using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
                using var cmd = conn.CreateRetrySqlCommand();
                cmd.CommandText = "IF object_id('dbo.Parameters') IS NOT NULL SELECT Number FROM dbo.Parameters WHERE Id = @Id"; // call can be made before store is initialized
                cmd.Parameters.AddWithValue("@Id", FlagId);
                var value = cmd.ExecuteScalarAsync(CancellationToken.None).Result;
                return value != null && (double)value == 1;
            }
        }
    }
}
