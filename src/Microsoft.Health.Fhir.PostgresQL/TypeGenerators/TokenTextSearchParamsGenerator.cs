// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;
using static Microsoft.Health.Fhir.PostgresQL.TypeConvert;

namespace Microsoft.Health.Fhir.PostgresQL.TypeGenerators
{
    internal class TokenTextSearchParamsGenerator
    {
        private readonly ISqlServerFhirModel _model;

        public TokenTextSearchParamsGenerator(ISqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));

            _model = model;
        }

        public static Type GetSearchValueType(SearchParameterInfo searchParameter)
        {
            if (searchParameter.Type != SearchParamType.Composite)
            {
                return GetSearchValueTypeForSearchParameterType(searchParameter.Type);
            }
            else
            {
                throw new NotSupportedException("not support type");
            }
        }

        private static Type GetSearchValueTypeForSearchParameterType(SearchParamType searchParameterType) =>
            searchParameterType switch
            {
                SearchParamType.Number => typeof(NumberSearchValue),
                SearchParamType.Date => typeof(DateTimeSearchValue),
                SearchParamType.String => typeof(StringSearchValue),
                SearchParamType.Token => typeof(TokenSearchValue),
                SearchParamType.Reference => typeof(ReferenceSearchValue),
                SearchParamType.Quantity => typeof(QuantitySearchValue),
                SearchParamType.Uri => typeof(UriSearchValue),
                _ => throw new ArgumentOutOfRangeException(nameof(searchParameterType)),
            };

        private static Type? GetSearchValueType(SearchIndexEntry searchIndexEntry)
        {
            if (searchIndexEntry.Value is CompositeSearchValue)
            {
                return null;
            }

            Type searchValueType = searchIndexEntry.Value.GetType();

            Debug.Assert(searchValueType == GetSearchValueType(searchIndexEntry.SearchParameter), "Getting the search value type from the search parameter produced a different result from calling searchValue.GetType()");

            return searchValueType;
        }

        public IEnumerable<BulkTokenTextTableTypeV1Row> GenerateRows(IReadOnlyList<ResourceWrapper> resources)
        {
            for (var index = 0; index < resources.Count; index++)
            {
                ResourceWrapper resource = resources[index];
                var searchIndices = resource.SearchIndices?.ToLookup(e => GetSearchValueType(e));

                foreach (SearchIndexEntry v in searchIndices == null ? Enumerable.Empty<SearchIndexEntry>() : searchIndices[typeof(TokenSearchValue)])
                {
                    short searchParamId = _model.GetSearchParamId(v.SearchParameter.Url);

                    foreach (var searchValue in new[] { (TokenSearchValue)v.Value})
                    {
                        if (TryGenerateRow(index, searchParamId, searchValue, out BulkTokenTextTableTypeV1Row row))
                        {
                            yield return row;
                        }
                    }
                }
            }
        }

        public static bool TryGenerateRow(int offset, short searchParamId, TokenSearchValue searchValue, out BulkTokenTextTableTypeV1Row row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = new BulkTokenTextTableTypeV1Row();
                return false;
            }

            row = new BulkTokenTextTableTypeV1Row()
            {
                offsetid = offset,
                searchparamid = searchParamId,
                text = searchValue.Text,
            };
            return true;
        }
    }
}
