// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenNumberNumberCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3), TokenNumberNumberCompositeSearchParamListRow>
    {
        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;
        private readonly NumberSearchParamListRowGenerator _numberRowGenerator;

        public TokenNumberNumberCompositeSearchParamListRowGenerator(SqlServerFhirModel model, TokenSearchParamListRowGenerator tokenRowGenerator, NumberSearchParamListRowGenerator numberRowGenerator, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _numberRowGenerator = numberRowGenerator;
        }

        internal override bool TryGenerateRow(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            (TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3) searchValue,
            HashSet<TokenNumberNumberCompositeSearchParamListRow> results,
            out TokenNumberNumberCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, null, out var token1Row) &&
                _numberRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, null, out var token2Row) &&
                _numberRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component3, null, out var token3Row))
            {
                bool hasRange = token2Row.SingleValue == null || token3Row.SingleValue == null;
                row = new TokenNumberNumberCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token1Row.CodeOverflow,
                    hasRange ? null : token2Row.SingleValue,
                    token2Row.LowValue ?? token2Row.SingleValue,
                    token2Row.HighValue ?? token2Row.SingleValue,
                    hasRange ? null : token3Row.SingleValue,
                    token3Row.LowValue ?? token3Row.SingleValue,
                    token3Row.HighValue ?? token3Row.SingleValue,
                    HasRange: hasRange);

                return results == null || results.Add(row);
            }

            row = default;
            return false;
        }
    }
}
