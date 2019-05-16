// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;
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

        protected override V1.StringSearchParamTableTypeRow GenerateRow(short searchParamId, SearchParameter searchParameter, StringSearchValue searchValue)
        {
            return new V1.StringSearchParamTableTypeRow(searchParamId, searchValue.String);
        }
    }
}
