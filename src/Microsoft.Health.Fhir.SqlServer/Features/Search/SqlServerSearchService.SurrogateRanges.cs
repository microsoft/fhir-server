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
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Parameters;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    /// <summary>
    /// SQL Server search service implementation.
    /// </summary>
    internal partial class SqlServerSearchService
    {
        private static bool ContainsStartSurrogateId(SqlSearchOptions options)
        {
            IReadOnlyList<(string Param, string Value)> hints = options.QueryHints;
            return hints.Any(x => string.Equals(KnownQueryParameterNames.StartSurrogateId, x.Param, StringComparison.OrdinalIgnoreCase));
        }

        private void PopulateSqlCommandFromQueryHints(SqlSearchOptions options, SqlCommand command)
        {
            IReadOnlyList<(string Param, string Value)> hints = options.QueryHints;

            var resourceTypeId = _model.GetResourceTypeId(hints.First(x => x.Param == KnownQueryParameterNames.Type).Value);
            var startId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.StartSurrogateId).Value);
            var endId = long.Parse(hints.First(x => x.Param == KnownQueryParameterNames.EndSurrogateId).Value);
            var globalStr = hints.FirstOrDefault(x => x.Param == KnownQueryParameterNames.GlobalEndSurrogateId).Value;
            var globalEndId = string.IsNullOrEmpty(globalStr) ? null : (long?)long.Parse(globalStr);

            PopulateSqlCommandFromQueryHints(command, resourceTypeId, startId, endId, globalEndId, options.ResourceVersionTypes.HasFlag(ResourceVersionType.History), options.ResourceVersionTypes.HasFlag(ResourceVersionType.SoftDeleted));
        }

        private static void PopulateSqlCommandFromQueryHints(SqlCommand command, short resourceTypeId, long startId, long endId, long? globalEndId, bool? includeHistory, bool? includeDeleted)
        {
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "dbo.GetResourcesByTypeAndSurrogateIdRange";
            command.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            command.Parameters.AddWithValue("@StartId", startId);
            command.Parameters.AddWithValue("@EndId", endId);
            if (globalEndId.HasValue)
            {
                command.Parameters.AddWithValue("@GlobalEndId", globalEndId.Value);
            }

            command.Parameters.AddWithValue("@IncludeHistory", includeHistory);
            command.Parameters.AddWithValue("@IncludeDeleted", includeDeleted);
        }

        public override Task<SearchResult> SearchBySurrogateIdRange(string resourceType, long startId, long endId, CancellationToken cancellationToken)
        {
            return SearchBySurrogateIdRange(resourceType, startId, endId, null, null, cancellationToken, false, false);
        }

        /// <summary>
        /// Searches for resources by their type and surrogate id and optionally a searchParamHash and will return resources
        /// </summary>
        /// <param name="resourceType">The resource type to search</param>
        /// <param name="startId">The lower bound for surrogate ids to find</param>
        /// <param name="endId">The upper bound for surrogate ids to find</param>
        /// <param name="windowStartId">The lower bound for the window of time to consider for historical records</param>
        /// <param name="windowEndId">The upper bound for the window of time to consider for historical records</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="includeHistory">Return historical records that match the other parameters.</param>
        /// <param name="includeDeleted">Return deleted records that match the other parameters.</param>
        /// <returns>All resources with surrogate ids greater than or equal to startId and less than or equal to endId. If windowEndId is set it will return the most recent version of a resource that was created before windowEndId that is within the range of startId to endId.</returns>
        public async Task<SearchResult> SearchBySurrogateIdRange(string resourceType, long startId, long endId, long? windowStartId, long? windowEndId, CancellationToken cancellationToken, bool includeHistory = false, bool includeDeleted = false)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            using var sqlCommand = new SqlCommand();
            sqlCommand.CommandTimeout = GetSurrogateIdRangeCommandTimeout();
            PopulateSqlCommandFromQueryHints(sqlCommand, resourceTypeId, startId, endId, windowEndId, includeHistory, includeDeleted);
            LogSqlCommand(sqlCommand);
            List<SearchResultEntry> resources = null;
            await _sqlRetryService.ExecuteSql(
                sqlCommand,
                async (cmd, cancel) =>
                {
                    using SqlDataReader reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancel);
                    resources = new List<SearchResultEntry>();
                    while (await reader.ReadAsync(cancel))
                    {
                        ReadWrapper(
                            reader,
                            true,
                            out short _,
                            out string resourceId,
                            out int version,
                            out bool isDeleted,
                            out long resourceSurrogateId,
                            out string requestMethod,
                            out bool isMatch,
                            out bool isPartialEntry,
                            out bool isRawResourceMetaSet,
                            out string searchParameterHash,
                            out byte[] rawResourceBytes,
                            out bool isInvisible,
                            out bool isHistory);

                        if (isInvisible)
                        {
                            continue;
                        }

                        using var rawResourceStream = new MemoryStream(rawResourceBytes);
                        var rawResource = _compressedRawResourceConverter.ReadCompressedRawResource(rawResourceStream);

                        if (string.IsNullOrEmpty(rawResource))
                        {
                            rawResource = MissingResourceFactory.CreateJson(resourceId, _model.GetResourceTypeName(resourceTypeId), "warning", "incomplete");
                            _requestContextAccessor.SetMissingResourceCode(System.Net.HttpStatusCode.PartialContent);
                        }

                        resources.Add(new SearchResultEntry(
                            new ResourceWrapper(
                                resourceId,
                                version.ToString(CultureInfo.InvariantCulture),
                                resourceType,
                                new RawResource(rawResource, FhirResourceFormat.Json, isMetaSet: isRawResourceMetaSet),
                                new ResourceRequest(requestMethod),
                                resourceSurrogateId.ToLastUpdated(),
                                isDeleted,
                                null,
                                null,
                                null,
                                searchParameterHash,
                                resourceSurrogateId)
                            {
                                IsHistory = isHistory,
                            },
                            isMatch ? SearchEntryMode.Match : SearchEntryMode.Include));
                    }

                    return;
                },
                _logger,
                null,
                cancellationToken);
            return new SearchResult(resources, null, null, new List<Tuple<string, string>>()) { TotalCount = resources.Count };
        }

        private static (long StartId, long EndId, int Count) ReaderToSurrogateIdRange(SqlDataReader sqlDataReader)
        {
            return (sqlDataReader.GetInt64(1), sqlDataReader.GetInt64(2), sqlDataReader.GetInt32(3));
        }

        public override async Task<IReadOnlyList<(long StartId, long EndId, int Count)>> GetSurrogateIdRanges(string resourceType, long startId, long endId, int rangeSize, int numberOfRanges, bool up, CancellationToken cancellationToken, bool activeOnly = false)
        {
            var resourceTypeId = _model.GetResourceTypeId(resourceType);
            using var sqlCommand = new SqlCommand();
            PopulateGetResourceSurrogateIdRangesCommand(sqlCommand, resourceTypeId, startId, endId, rangeSize, numberOfRanges, up, activeOnly);
            sqlCommand.CommandTimeout = GetSurrogateIdRangeCommandTimeout();
            LogSqlCommand(sqlCommand);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderToSurrogateIdRange, _logger, cancellationToken);
        }

        private static string ReaderGetUsedResourceTypes(SqlDataReader sqlDataReader)
        {
            return sqlDataReader.GetString(1);
        }

        public override async Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken)
        {
            using var sqlCommand = new SqlCommand("dbo.GetUsedResourceTypes") { CommandType = CommandType.StoredProcedure };
            LogSqlCommand(sqlCommand);
            return await sqlCommand.ExecuteReaderAsync(_sqlRetryService, ReaderGetUsedResourceTypes, _logger, cancellationToken);
        }

        private int GetSurrogateIdRangeCommandTimeout()
        {
            return Math.Max((int)_sqlServerDataStoreConfiguration.CommandTimeout.TotalSeconds, 1200);
        }

        private static void PopulateGetResourceSurrogateIdRangesCommand(SqlCommand cmd, short resourceTypeId, long startId, long endId, int rangeSize, int? numberOfRanges, bool up, bool activeOnly)
        {
            cmd.CommandText = "dbo.GetResourceSurrogateIdRanges";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@StartId", startId);
            cmd.Parameters.AddWithValue("@EndId", endId);
            cmd.Parameters.AddWithValue("@RangeSize", rangeSize);
            cmd.Parameters.AddWithValue("@NumberOfRanges", numberOfRanges);
            cmd.Parameters.AddWithValue("@Up", up);
            cmd.Parameters.AddWithValue("@ActiveOnly", activeOnly);
        }
    }
}
