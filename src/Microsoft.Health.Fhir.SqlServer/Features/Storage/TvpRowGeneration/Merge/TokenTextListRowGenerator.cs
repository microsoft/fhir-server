// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenTextListRowGenerator : MergeSearchParameterRowGenerator<TokenSearchValue, TokenTextListRow>
    {
        public TokenTextListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, TokenSearchValue searchValue, HashSet<TokenTextListRow> results, out TokenTextListRow row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = default;
                return false;
            }

            row = new TokenTextListRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.Text.ToLower(CultureInfo.InvariantCulture));
            return results == null || results.Add(row);
        }
    }
}
