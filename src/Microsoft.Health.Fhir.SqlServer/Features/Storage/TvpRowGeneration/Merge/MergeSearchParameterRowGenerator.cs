// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal abstract class MergeSearchParameterRowGenerator<TSearchValue, TRow> : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, TRow>
        where TRow : struct
    {
        private const int LargeSearchIndexCount = 10000;
        private const int GeneratedRowLogInterval = 100000;

        private static readonly string GeneratorName = typeof(TRow).Name;

        private static readonly Action<ILogger, string, string, string, int, Exception> LogLargeSearchIndexCount =
            LoggerMessage.Define<string, string, string, int>(
                LogLevel.Warning,
                new EventId(62000, nameof(LogLargeSearchIndexCount)),
                "Large search parameter TVP input detected. Generator={Generator}, ResourceType={ResourceType}, ResourceId={ResourceId}, SearchIndexCount={SearchIndexCount}.");

        private static readonly Action<ILogger, string, string, string, Uri, int, Exception> LogGeneratedRowProgress =
            LoggerMessage.Define<string, string, string, Uri, int>(
                LogLevel.Warning,
                new EventId(62001, nameof(LogGeneratedRowProgress)),
                "Search parameter TVP generated a large number of rows for one resource. Generator={Generator}, ResourceType={ResourceType}, ResourceId={ResourceId}, SearchParameterUrl={SearchParameterUrl}, GeneratedRowCount={GeneratedRowCount}.");

        private static readonly Action<ILogger, string, string, string, Uri, int, int, Exception> LogOutOfMemory =
            LoggerMessage.Define<string, string, string, Uri, int, int>(
                LogLevel.Error,
                new EventId(62002, nameof(LogOutOfMemory)),
                "Out of memory while generating search parameter TVP rows. Generator={Generator}, ResourceType={ResourceType}, ResourceId={ResourceId}, SearchParameterUrl={SearchParameterUrl}, SearchIndexCount={SearchIndexCount}, GeneratedRowCount={GeneratedRowCount}.");

        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly bool _isConvertSearchValueOverridden;
        private bool _isInitialized;

        protected MergeSearchParameterRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            Model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _isConvertSearchValueOverridden = GetType().GetMethod(nameof(ConvertSearchValue), BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(MergeSearchParameterRowGenerator<TSearchValue, TRow>);
        }

        protected SqlServerFhirModel Model { get; }

        public virtual IEnumerable<TRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            return GenerateRowsCore(resources, diagnostics: null);
        }

        internal IEnumerable<TRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources, ILogger logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            return GenerateRowsWithDiagnostics(resources, logger);
        }

        private IEnumerable<TRow> GenerateRowsWithDiagnostics(IReadOnlyList<MergeResourceWrapper> resources, ILogger logger)
        {
            var diagnostics = new RowGenerationDiagnostics
            {
                Logger = logger,
            };
            using IEnumerator<TRow> enumerator = GenerateRowsCore(resources, diagnostics).GetEnumerator();

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = enumerator.MoveNext();
                }
                catch (OutOfMemoryException exception)
                {
                    LogOutOfMemory(
                        logger,
                        GeneratorName,
                        diagnostics.ResourceType,
                        diagnostics.ResourceId,
                        diagnostics.SearchParameterUrl,
                        diagnostics.SearchIndexCount,
                        diagnostics.GeneratedRowCount,
                        exception);
                    throw;
                }

                if (!hasNext)
                {
                    yield break;
                }

                diagnostics.GeneratedRowCount++;
                if (diagnostics.GeneratedRowCount % GeneratedRowLogInterval == 0)
                {
                    LogGeneratedRowProgress(
                        logger,
                        GeneratorName,
                        diagnostics.ResourceType,
                        diagnostics.ResourceId,
                        diagnostics.SearchParameterUrl,
                        diagnostics.GeneratedRowCount,
                        null);
                }

                yield return enumerator.Current;
            }
        }

        private IEnumerable<TRow> GenerateRowsCore(IReadOnlyList<MergeResourceWrapper> resources, RowGenerationDiagnostics diagnostics)
        {
            EnsureInitialized();

            foreach (var merge in resources)
            {
                if (merge.ResourceWrapper.IsHistory)
                {
                    continue;
                }

                if (diagnostics != null)
                {
                    diagnostics.ResourceType = merge.ResourceWrapper.ResourceTypeName;
                    diagnostics.ResourceId = merge.ResourceWrapper.ResourceId;
                    diagnostics.SearchParameterUrl = null;
                    diagnostics.SearchIndexCount = merge.ResourceWrapper.SearchIndices?.Count ?? 0;
                    diagnostics.GeneratedRowCount = 0;

                    if (diagnostics.SearchIndexCount >= LargeSearchIndexCount)
                    {
                        LogLargeSearchIndexCount(
                            diagnostics.Logger,
                            GeneratorName,
                            diagnostics.ResourceType,
                            diagnostics.ResourceId,
                            diagnostics.SearchIndexCount,
                            null);
                    }
                }

                var typeId = Model.GetResourceTypeId(merge.ResourceWrapper.ResourceTypeName);
                var resourceMetadata = new ResourceMetadata(
                        merge.ResourceWrapper.CompartmentIndices,
                        merge.ResourceWrapper.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                        merge.ResourceWrapper.LastModifiedClaims);

                var resultsForDedupping = new HashSet<TRow>();

                foreach (SearchIndexEntry v in resourceMetadata.GetSearchIndexEntriesByType(typeof(TSearchValue)))
                {
                    if (diagnostics != null)
                    {
                        diagnostics.SearchParameterUrl = v.SearchParameter.Url;
                    }

                    short searchParamId = Model.GetSearchParamId(v.SearchParameter.Url);

                    if (!_isConvertSearchValueOverridden)
                    {
                        var searchValue = (TSearchValue)v.Value;

                        // save an array allocation
                        if (TryGenerateRow(typeId, merge.ResourceWrapper.ResourceSurrogateId, searchParamId, searchValue, resultsForDedupping, out TRow row))
                        {
                            yield return row;
                        }
                    }
                    else
                    {
                        foreach (var searchValue in ConvertSearchValue(v))
                        {
                            if (TryGenerateRow(typeId, merge.ResourceWrapper.ResourceSurrogateId, searchParamId, searchValue, resultsForDedupping, out TRow row))
                            {
                                yield return row;
                            }
                        }
                    }
                }
            }
        }

        protected void EnsureInitialized()
        {
            if (Volatile.Read(ref _isInitialized))
            {
                return;
            }

            Initialize();

            Volatile.Write(ref _isInitialized, true);
        }

        protected virtual IEnumerable<TSearchValue> ConvertSearchValue(SearchIndexEntry entry) => new[] { (TSearchValue)entry.Value };

        protected virtual void Initialize()
        {
        }

        internal abstract bool TryGenerateRow(short resourceTypeId, long resourceRecordId, short searchParamId, TSearchValue searchValue, HashSet<TRow> results, out TRow row);

        private sealed class RowGenerationDiagnostics
        {
            public ILogger Logger { get; set; }

            public string ResourceType { get; set; }

            public string ResourceId { get; set; }

            public Uri SearchParameterUrl { get; set; }

            public int SearchIndexCount { get; set; }

            public int GeneratedRowCount { get; set; }
        }
    }
}
