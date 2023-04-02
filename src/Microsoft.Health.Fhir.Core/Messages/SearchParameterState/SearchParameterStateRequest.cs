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
        public SearchParameterStateRequest(ICollection<string> searchParameterId = null, ICollection<string> resourceType = null, ICollection<string> code = null, ICollection<string> urls = null, IReadOnlyList<Tuple<string, string>> queries = null)
        {
            SearchParameterId = searchParameterId;
            Queries = queries;
            ResourceTypes = resourceType;
            Codes = code;
            Urls = urls;
        }

        public IReadOnlyList<Tuple<string, string>> Queries { get; }

        public ICollection<string> SearchParameterId { get; }

        public ICollection<string> ResourceTypes { get; }

        public ICollection<string> Codes { get; }

        public ICollection<string> Urls { get; }
    }
}
