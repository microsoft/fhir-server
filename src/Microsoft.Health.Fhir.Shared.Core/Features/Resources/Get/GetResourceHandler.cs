// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Get
{
    public class GetResourceHandler : BaseResourceHandler, IRequestHandler<GetResourceRequest, GetResourceResponse>
    {
        private readonly IDataResourceFilter _dataResourceFilter;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchService _searchService;

        public GetResourceHandler(
            IFhirDataStore fhirDataStore,
            Lazy<IConformanceProvider> conformanceProvider,
            IResourceWrapperFactory resourceWrapperFactory,
            ResourceIdProvider resourceIdProvider,
            IDataResourceFilter dataResourceFilter,
            IAuthorizationService<DataActions> authorizationService,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ISearchService searchService)
            : base(fhirDataStore, conformanceProvider, resourceWrapperFactory, resourceIdProvider, authorizationService)
        {
            _dataResourceFilter = EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));
            _contextAccessor = EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

        public async Task<GetResourceResponse> Handle(GetResourceRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await AuthorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            var key = request.ResourceKey;

            ResourceWrapper currentDoc = null;

            // if this is a smart request we need to ensure that this resource is within the resources allowed to the user
            // convert the request into a search request
            if (_contextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
            {
                var query = new List<Tuple<string, string>>();
                query.Add(new Tuple<string, string>(KnownQueryParameterNames.Id, key.Id));

                var results = await _searchService.SearchAsync(key.ResourceType, query, cancellationToken);

                if (results.Results.Any())
                {
                    currentDoc = results.Results?.FirstOrDefault().Resource;
                }
            }
            else
            {
                currentDoc = await FhirDataStore.GetAsync(key, cancellationToken);
            }

            if (currentDoc == null)
            {
                if (string.IsNullOrEmpty(key.VersionId))
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, key.ResourceType, key.Id));
                }
                else
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundByIdAndVersion, key.ResourceType, key.Id, key.VersionId));
                }
            }

            if (currentDoc.IsHistory &&
                ConformanceProvider != null &&
                await ConformanceProvider.Value.CanReadHistory(key.ResourceType, cancellationToken) == false)
            {
                throw new MethodNotAllowedException(string.Format(Core.Resources.ReadHistoryDisabled, key.ResourceType));
            }

            if (currentDoc.IsDeleted)
            {
                // As per FHIR Spec if the resource was marked as deleted on that version or the latest is marked as deleted then
                // we need to return a resource gone message.
                throw new ResourceGoneException(new ResourceKey(currentDoc.ResourceTypeName, currentDoc.ResourceId, currentDoc.Version));
            }

            FilterCriteriaOutcome filterOutcome = _dataResourceFilter.Match(currentDoc);
            if (!filterOutcome.Match)
            {
                // As per US Core Spec, if the resource is not compliant, it should return a Not Found.
                throw new ResourceNotFoundException(filterOutcome.OutcomeIssue.Diagnostics);
            }

            return new GetResourceResponse(new RawResourceElement(currentDoc));
        }
    }
}
