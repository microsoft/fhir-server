// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.SearchParameterState
{
    public class SearchParameterStateResponse
    {
        public SearchParameterStateResponse(ResourceElement searchParameterInfos = null)
        {
            SearchParameters = searchParameterInfos;
        }

        public ResourceElement SearchParameters { get; }
    }
}
