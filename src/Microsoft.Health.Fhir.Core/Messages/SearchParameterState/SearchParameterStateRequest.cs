// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.SearchParameterState
{
    public class SearchParameterStateRequest : IRequest<SearchParameterStateResponse>
    {
        public SearchParameterStateRequest(IReadOnlyList<Tuple<string, string>> queries = null)
        {
            Queries = queries;
        }

        public IReadOnlyList<Tuple<string, string>> Queries { get; }
    }
}
