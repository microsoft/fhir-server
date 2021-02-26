// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class SearchParameterInfoExtensions
    {
        /// <summary>
        /// Determines the location of the column(s) for a given search parameter are located.
        /// </summary>
        /// <param name="searchParameter">The search parameter</param>
        /// <returns>The location enum value. Note this is a flags enum</returns>
        public static SearchParameterColumnLocation ColumnLocation(this SearchParameterInfo searchParameter)
        {
            switch (searchParameter.Code)
            {
                case SearchParameterNames.LastUpdated:
                case SqlSearchParameters.ResourceSurrogateIdParameterName:
                case SearchParameterNames.ResourceType:
                    return SearchParameterColumnLocation.ResourceTable | SearchParameterColumnLocation.SearchParamTable;
                case SearchParameterNames.Id:
                    return SearchParameterColumnLocation.ResourceTable;
                default:
                    return SearchParameterColumnLocation.SearchParamTable;
            }
        }
    }
}
