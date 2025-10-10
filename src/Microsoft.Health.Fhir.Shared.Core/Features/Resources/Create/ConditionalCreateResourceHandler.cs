// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;

using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Create
{
    public sealed class ConditionalCreateResourceHandler : ConditionalResourceHandler<ConditionalCreateResourceRequest, UpsertResourceResponse>
    {
        private readonly IMediator _mediator;

        public ConditionalCreateResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ISearchService searchService,
            IMediator mediator,
            ResourceIdProvider resourceIdProvider,
            IAuthorizationService<DataActions> authorizationService,
            ILogger<ConditionalCreateResourceHandler> logger)
            : base(searchService, fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService, logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
        }

        public override async Task<UpsertResourceResponse> HandleNoMatch(ConditionalCreateResourceRequest request, CancellationToken cancellationToken)
        {
            return await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(request.Resource, request.BundleResourceContext), cancellationToken);
        }

        public override Task<UpsertResourceResponse> HandleSingleMatch(ConditionalCreateResourceRequest request, SearchResultEntry match, CancellationToken cancellationToken)
        {
            var saveOutcome = new SaveOutcome(new Models.RawResourceElement(match.Resource), SaveOutcomeType.MatchFound);
            return Task.FromResult<UpsertResourceResponse>(new UpsertResourceResponse(saveOutcome));
        }

        /// <summary>
        /// Conditional create requires search permissions (to find existing resources) and create permissions (for new resources).
        /// Legacy: Read + Write, SMART v2: Search + Create
        /// </summary>
        protected override (DataActions legacyPermissions, DataActions granularPermissions) GetRequiredPermissions(ConditionalCreateResourceRequest request)
        {
            return (DataActions.Read | DataActions.Write, DataActions.Search | DataActions.Create);
        }
    }
}
