// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public class SearchResourceHistoryHandler : IRequestHandler<SearchResourceHistoryRequest, SearchResourceHistoryResponse>
    {
        private readonly ISearchService _searchService;

        public SearchResourceHistoryHandler(ISearchService searchService)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));

            _searchService = searchService;
        }

        public async Task<SearchResourceHistoryResponse> Handle(SearchResourceHistoryRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            Bundle bundle = await _searchService.SearchHistoryAsync(
                message.ResourceType,
                message.ResourceId,
                message.At,
                message.Since,
                message.Count,
                message.ContinuationToken,
                cancellationToken);

            Debug.Assert(bundle != null, "SearchService should not return null bundle.");

            return new SearchResourceHistoryResponse(bundle);
        }
    }
}
