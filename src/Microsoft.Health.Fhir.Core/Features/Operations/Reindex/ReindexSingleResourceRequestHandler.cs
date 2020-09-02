// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexSingleResourceRequestHandler : IRequestHandler<ReindexSingleResourceRequest, ReindexSingleResourceResponse>
    {
        private readonly IFhirAuthorizationService _authorizationService;
        private readonly IFhirDataStore _fhirDataStore;
        private readonly ISearchIndexer _searchIndexer;

        public ReindexSingleResourceRequestHandler(
            IFhirAuthorizationService authorizationService,
            IFhirDataStore fhirDataStore,
            ISearchIndexer searchIndexer)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));
            EnsureArg.IsNotNull(searchIndexer, nameof(searchIndexer));

            _authorizationService = authorizationService;
            _fhirDataStore = fhirDataStore;
            _searchIndexer = searchIndexer;
        }

        public async Task<ReindexSingleResourceResponse> Handle(ReindexSingleResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            var key = new ResourceKey(request.ResourceType, request.ResourceId);
            ResourceWrapper storedResource = await _fhirDataStore.GetAsync(key, cancellationToken);

            throw new System.NotImplementedException();
        }
    }
}
