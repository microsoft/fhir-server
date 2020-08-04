// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class ExtractReindexRequest : IRequest<ExtractReindexResponse>
    {
        public ExtractReindexRequest(SearchResult result, string hash)
        {
            Result = result;
            HashValue = hash;
        }

        public SearchResult Result { get; }

        public string HashValue { get; }
    }
}
