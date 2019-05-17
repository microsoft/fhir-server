// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal abstract class SearchParameterRowGenerator<TSearchValue, TRow> : ITableValuedParameterRowGenerator<ResourceMetadata, TRow>
        where TRow : struct
    {
        protected SearchParameterRowGenerator(SqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));
            Model = model;
        }

        protected SqlServerFhirModel Model { get; }

        public virtual IEnumerable<TRow> GenerateRows(ResourceMetadata input)
        {
            foreach (SearchIndexEntry v in input.GetSearchIndexEntriesByType(typeof(TSearchValue)))
            {
                foreach (var searchValue in ConvertSearchValue(v))
                {
                    if (TryGenerateRow(Model.GetSearchParamId(v.SearchParameter.Url.ToString()), searchValue, out TRow row))
                    {
                        yield return row;
                    }
                }
            }
        }

        protected virtual IEnumerable<TSearchValue> ConvertSearchValue(SearchIndexEntry entry) => new[] { (TSearchValue)entry.Value };

        internal abstract bool TryGenerateRow(short searchParamId, TSearchValue searchValue, out TRow row);
    }
}
