// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceTokenCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), ReferenceTokenCompositeSearchParamListRow>
    {
        private readonly ReferenceSearchParamListRowGenerator _referenceRowGenerator;
        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;

        public ReferenceTokenCompositeSearchParamListRowGenerator(
            SqlServerFhirModel model,
            ReferenceSearchParamListRowGenerator referenceRowGenerator,
            TokenSearchParamListRowGenerator tokenRowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _referenceRowGenerator = referenceRowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, HashSet<ReferenceTokenCompositeSearchParamListRow> results, out ReferenceTokenCompositeSearchParamListRow row)
        {
            if (_referenceRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.component1, null, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.component2, null, out var token2Row))
            {
                row = new ReferenceTokenCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    reference1Row.BaseUri,
                    reference1Row.ReferenceResourceTypeId,
                    reference1Row.ReferenceResourceId,
                    reference1Row.ReferenceResourceVersion,
                    token2Row.SystemId,
                    token2Row.Code,
                    token2Row.CodeOverflow);

                return results == null || results.Add(row);
            }

            row = default;
            return false;
        }
    }
}
