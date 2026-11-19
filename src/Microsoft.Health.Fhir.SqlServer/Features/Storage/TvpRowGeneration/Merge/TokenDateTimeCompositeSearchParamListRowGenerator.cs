// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenDateTimeCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, DateTimeSearchValue component2), TokenDateTimeCompositeSearchParamListRow>
    {
        private static readonly IEqualityComparer<TokenDateTimeCompositeSearchParamListRow> RowComparer = EqualityComparer<TokenDateTimeCompositeSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.SystemId1 == right.SystemId1 &&
                string.Equals(left.Code1, right.Code1, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow1, right.CodeOverflow1, StringComparison.Ordinal) &&
                left.StartDateTime2 == right.StartDateTime2 &&
                left.EndDateTime2 == right.EndDateTime2 &&
                left.IsLongerThanADay2 == right.IsLongerThanADay2,
            row =>
            {
                var hash = default(HashCode);
                hash.Add(row.ResourceTypeId);
                hash.Add(row.ResourceSurrogateId);
                hash.Add(row.SearchParamId);
                hash.Add(row.SystemId1);
                hash.Add(row.Code1);
                hash.Add(row.CodeOverflow1);
                hash.Add(row.StartDateTime2);
                hash.Add(row.EndDateTime2);
                hash.Add(row.IsLongerThanADay2);
                return hash.ToHashCode();
            });

        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;
        private readonly DateTimeSearchParamListRowGenerator _dateTimeRowGenerator;

        public TokenDateTimeCompositeSearchParamListRowGenerator(
            SqlServerFhirModel model,
            TokenSearchParamListRowGenerator tokenRowGenerator,
            DateTimeSearchParamListRowGenerator dateTimeV1RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _dateTimeRowGenerator = dateTimeV1RowGenerator;
        }

        internal override bool TryGenerateRow(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            (TokenSearchValue component1, DateTimeSearchValue component2) searchValue,
            HashSet<TokenDateTimeCompositeSearchParamListRow> results,
            out TokenDateTimeCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, null, out var token1Row) &&
                _dateTimeRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, null, out var token2Row))
            {
                row = new TokenDateTimeCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token1Row.CodeOverflow,
                    token2Row.StartDateTime,
                    token2Row.EndDateTime,
                    token2Row.IsLongerThanADay);

                return results == null || results.Add(row);
            }

            row = default;
            return false;
        }
    }
}
