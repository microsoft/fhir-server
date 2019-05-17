// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class StringSearchParameterRowGenerator : SearchParameterRowGenerator<StringSearchValue, V1.StringSearchParamTableTypeRow>
    {
        public StringSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, StringSearchValue searchValue, out V1.StringSearchParamTableTypeRow row)
        {
            row = new V1.StringSearchParamTableTypeRow(searchParamId, searchValue.String);
            return true;
        }
    }
}
