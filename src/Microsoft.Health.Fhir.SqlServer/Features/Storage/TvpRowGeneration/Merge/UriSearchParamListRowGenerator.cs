// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class UriSearchParamListRowGenerator : MergeSearchParameterRowGenerator<UriSearchValue, UriSearchParamListRow>
    {
        public UriSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, UriSearchValue searchValue, HashSet<UriSearchParamListRow> results, out UriSearchParamListRow row)
        {
            row = new UriSearchParamListRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.Uri);
            return results == null || results.Add(row);
        }
    }
}
