// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Core.Messages.SearchParameterState
{
    public class SearchParameterStateUpdateRequest : IRequest<SearchParameterStateUpdateResponse>
    {
        public SearchParameterStateUpdateRequest(IEnumerable<Tuple<Uri, SearchParameterStatus>> searchParameters)
        {
            SearchParameters = searchParameters;
        }

        public IEnumerable<Tuple<Uri, SearchParameterStatus>> SearchParameters { get; }
    }
}
