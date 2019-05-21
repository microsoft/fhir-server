// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class ReferenceTokenCompositeSearchParameterRowGenerator : CompositeSearchParameterRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), V1.ReferenceTokenCompositeSearchParamTableTypeRow>
    {
        private readonly ReferenceSearchParameterRowGenerator _referenceRowGenerator;
        private readonly TokenSearchParameterRowGenerator _tokenRowGenerator;

        public ReferenceTokenCompositeSearchParameterRowGenerator(
            SqlServerFhirModel model,
            ReferenceSearchParameterRowGenerator referenceRowGenerator,
            TokenSearchParameterRowGenerator tokenRowGenerator)
            : base(model)
        {
            _referenceRowGenerator = referenceRowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, out V1.ReferenceTokenCompositeSearchParamTableTypeRow row)
        {
            if (_referenceRowGenerator.TryGenerateRow(default, searchValue.component1, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row))
            {
                row = new V1.ReferenceTokenCompositeSearchParamTableTypeRow(
                    searchParamId,
                    reference1Row.BaseUri,
                    reference1Row.ReferenceResourceTypeId,
                    reference1Row.ReferenceResourceId,
                    reference1Row.ReferenceResourceVersion,
                    token2Row.SystemId,
                    token2Row.Code);

                return true;
            }

            row = default;
            return false;
        }
    }
}
