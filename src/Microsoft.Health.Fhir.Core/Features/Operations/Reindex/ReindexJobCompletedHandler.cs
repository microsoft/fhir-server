// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public class ReindexJobCompletedHandler : IRequestHandler<ReindexJobCompletedRequest, ReindexJobCompletedResponse>
    {
        private readonly ISupportedSearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly IFhirAuthorizationService _authorizationService;

        public ReindexJobCompletedHandler(
            ISupportedSearchParameterDefinitionManager searchParameterDefinitionManager,
            ISearchParameterRegistry searchParameterRegistry,
            IFhirAuthorizationService fhirAuthorizationService)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterRegistry, nameof(searchParameterRegistry));
            EnsureArg.IsNotNull(fhirAuthorizationService, nameof(fhirAuthorizationService));

            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterRegistry = searchParameterRegistry;
            _authorizationService = fhirAuthorizationService;
        }

        public async Task<ReindexJobCompletedResponse> Handle(ReindexJobCompletedRequest message, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.Reindex) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            var searchParameterStatusList = new List<ResourceSearchParameterStatus>();

            foreach (string uri in message.SearchParameterUris)
            {
                var searchParamUri = new Uri(uri);

                try
                {
                    _searchParameterDefinitionManager.SetSearchParameterEnabled(searchParamUri);
                }
                catch (SearchParameterNotSupportedException)
                {
                    return new ReindexJobCompletedResponse(false, Resources.SearchParameterNoLongerSupported);
                }

                searchParameterStatusList.Add(new ResourceSearchParameterStatus()
                {
                    LastUpdated = DateTimeOffset.UtcNow,
                    Status = SearchParameterStatus.Enabled,
                    Uri = searchParamUri,
                });
            }

            await _searchParameterRegistry.UpdateStatuses(searchParameterStatusList);

            return new ReindexJobCompletedResponse(true, null);
        }
    }
}
