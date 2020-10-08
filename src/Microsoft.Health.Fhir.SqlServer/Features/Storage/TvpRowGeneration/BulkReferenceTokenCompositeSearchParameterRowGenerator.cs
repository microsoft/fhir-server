// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkReferenceTokenCompositeSearchParameterRowGenerator : BulkCompositeSearchParameterRowGenerator<(ReferenceSearchValue component1, TokenSearchValue component2), VLatest.BulkReferenceTokenCompositeSearchParamTableTypeRow>
    {
        private readonly BulkReferenceSearchParameterRowGenerator _referenceRowGenerator;
        private readonly BulkTokenSearchParameterRowGenerator _tokenRowGenerator;

        public BulkReferenceTokenCompositeSearchParameterRowGenerator(
            SqlServerFhirModel model,
            BulkReferenceSearchParameterRowGenerator referenceRowGenerator,
            BulkTokenSearchParameterRowGenerator tokenRowGenerator)
            : base(model)
        {
            _referenceRowGenerator = referenceRowGenerator;
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(int id, short searchParamId, (ReferenceSearchValue component1, TokenSearchValue component2) searchValue, out VLatest.BulkReferenceTokenCompositeSearchParamTableTypeRow row)
        {
            if (_referenceRowGenerator.TryGenerateRow(id, default, searchValue.component1, out var reference1Row) &&
                _tokenRowGenerator.TryGenerateRow(id, default, searchValue.component2, out var token2Row))
            {
                row = new VLatest.BulkReferenceTokenCompositeSearchParamTableTypeRow(
                    id,
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
