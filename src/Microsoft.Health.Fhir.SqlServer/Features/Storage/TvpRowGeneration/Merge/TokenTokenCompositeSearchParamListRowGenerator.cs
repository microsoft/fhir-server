// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenTokenCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, TokenSearchValue component2), TokenTokenCompositeSearchParamListRow>
    {
        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;

        public TokenTokenCompositeSearchParamListRowGenerator(SqlServerFhirModel model, TokenSearchParamListRowGenerator tokenRowGenerator, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, (TokenSearchValue component1, TokenSearchValue component2) searchValue, out TokenTokenCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, out var token1Row) &&
                _tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, out var token2Row))
            {
                row = new TokenTokenCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token1Row.CodeOverflow,
                    token2Row.SystemId,
                    token2Row.Code,
                    token2Row.CodeOverflow);

                return true;
            }

            row = default;
            return false;
        }
    }
}
