// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal abstract class BulkSearchParameterRowGenerator<TSearchValue, TRow> : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceWrapper>, TRow>
        where TRow : struct
    {
        private readonly SearchParameterToSearchValueTypeMap _searchParameterTypeMap;
        private readonly bool _isConvertSearchValueOverridden;
        private bool _isInitialized;

        protected BulkSearchParameterRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            EnsureArg.IsNotNull(searchParameterTypeMap, nameof(searchParameterTypeMap));

            Model = model;
            _searchParameterTypeMap = searchParameterTypeMap;
            _isConvertSearchValueOverridden = GetType().GetMethod(nameof(ConvertSearchValue), BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(SearchParameterRowGenerator<TSearchValue, TRow>);
        }

        protected SqlServerFhirModel Model { get; }

        public virtual IEnumerable<TRow> GenerateRows(IReadOnlyList<ResourceWrapper> input)
        {
            EnsureInitialized();

            for (var index = 0; index < input.Count; index++)
            {
                ResourceWrapper resource = input[index];

                var resourceMetadata = new ResourceMetadata(
                    resource.CompartmentIndices,
                    resource.SearchIndices?.ToLookup(e => _searchParameterTypeMap.GetSearchValueType(e)),
                    resource.LastModifiedClaims);

                foreach (SearchIndexEntry v in resourceMetadata.GetSearchIndexEntriesByType(typeof(TSearchValue)))
                {
                    short searchParamId = Model.GetSearchParamId(v.SearchParameter.Url);

                    if (!_isConvertSearchValueOverridden)
                    {
                        // save an array allocation
                        if (TryGenerateRow(index, searchParamId, (TSearchValue)v.Value, out TRow row))
                        {
                            yield return row;
                        }
                    }
                    else
                    {
                        foreach (var searchValue in ConvertSearchValue(v))
                        {
                            if (TryGenerateRow(index, searchParamId, searchValue, out TRow row))
                            {
                                yield return row;
                            }
                        }
                    }
                }
            }
        }

        private void EnsureInitialized()
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

        internal abstract bool TryGenerateRow(int offset, short searchParamId, TSearchValue searchValue, out TRow row);
    }
}
