// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchResourceRequest : IRequest<SearchResourceResponse>
    {
        public SearchResourceRequest(string resourceType, IReadOnlyList<Tuple<string, string>> queries)
        {
            ResourceType = resourceType;
            Queries = queries;
        }

        public string ResourceType { get; }

        public IReadOnlyList<Tuple<string, string>> Queries { get; }
    }
}
