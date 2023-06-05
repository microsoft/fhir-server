// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal abstract class MergeSearchParameterRowGenerator<TSearchValue, TRow> : ITableValuedParameterRowGenerator<IReadOnlyList<MergeResourceWrapper>, TRow>
        where TRow : struct
    {
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly bool _isConvertSearchValueOverridden;
        private bool _isInitialized;

        protected MergeSearchParameterRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            Model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _isConvertSearchValueOverridden = GetType().GetMethod(nameof(ConvertSearchValue), BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(SearchParameterRowGenerator<TSearchValue, TRow>);
        }

        protected SqlServerFhirModel Model { get; }

        public virtual IEnumerable<TRow> GenerateRows(IReadOnlyList<MergeResourceWrapper> resources)
        {
            EnsureInitialized();

            foreach (var merge in resources.Where(_ => !_.ResourceWrapper.IsHistory)) // only current
            {
                var typeId = Model.GetResourceTypeId(merge.ResourceWrapper.ResourceTypeName);
                var resourceMetadata = new ResourceMetadata(
                        merge.ResourceWrapper.CompartmentIndices,
                        merge.ResourceWrapper.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                        merge.ResourceWrapper.LastModifiedClaims);

                var resultsForDedupping = new HashSet<TRow>();

                foreach (SearchIndexEntry v in resourceMetadata.GetSearchIndexEntriesByType(typeof(TSearchValue)))
                {
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
    }
}
