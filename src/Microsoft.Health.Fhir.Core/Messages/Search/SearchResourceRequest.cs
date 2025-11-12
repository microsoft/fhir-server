// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Medino;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class SearchResourceRequest : IRequest<SearchResourceResponse>
    {
        public SearchResourceRequest(string resourceType, IReadOnlyList<Tuple<string, string>> queries, bool isIncludesRequest = false)
        {
            ResourceType = resourceType;
            Queries = queries;
            IsIncludesRequest = isIncludesRequest;
        }

        public string ResourceType { get; }

        public IReadOnlyList<Tuple<string, string>> Queries { get; set; }

        public bool IsIncludesRequest { get; } = false;
    }
}
