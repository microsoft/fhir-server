// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.SearchParameterState
{
    public class SearchParameterStateRequest : IRequest<SearchParameterStateResponse>
    {
        public SearchParameterStateRequest(string searchParameterName, string searchParameterId)
        {
            SearchParameterName = searchParameterName;
            SearchParameterId = searchParameterId;
        }

        public string SearchParameterId { get; set; }

        public string SearchParameterName { get; set; }
    }
}
