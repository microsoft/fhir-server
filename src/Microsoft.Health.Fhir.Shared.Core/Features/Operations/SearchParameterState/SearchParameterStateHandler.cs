// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Parameters;

namespace Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState
{
    public class SearchParameterStateHandler : IRequestHandler<SearchParameterStateRequest, SearchParameterStateResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        public SearchParameterStateHandler(IAuthorizationService<DataActions> authorizationService, ISearchParameterDefinitionManager searchParameterDefinitionManager, SearchParameterStatusManager searchParameterStatusManager)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));

            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _searchParameterStatusManager = searchParameterStatusManager;
        }

        public async Task<SearchParameterStateResponse> Handle(SearchParameterStateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            IEnumerable<SearchParameterInfo> searchParameterResult = new List<SearchParameterInfo>();

            if (request.ResourceTypes != null)
            {
                searchParameterResult = _searchParameterDefinitionManager.GetSearchParametersByResourceTypes(request.ResourceTypes);
            }

            if (request.Codes != null)
            {
                var codeResults = _searchParameterDefinitionManager.GetSearchParametersByCodes(request.Codes);
                searchParameterResult = searchParameterResult.Any() ? searchParameterResult.IntersectBy(codeResults, sp => sp, new SearchParameterInfoEqualityComparer()) : codeResults;
            }

            if (request.Urls != null)
            {
                var urlResults = _searchParameterDefinitionManager.GetSearchParametersByUrls(request.Urls);
                searchParameterResult = searchParameterResult.Any() ? searchParameterResult.IntersectBy(urlResults, sp => sp, new SearchParameterInfoEqualityComparer()) : urlResults;
            }

            if (request.SearchParameterId != null)
            {
                var idResults = _searchParameterDefinitionManager.GetSearchParametersByIds(request.SearchParameterId);
                searchParameterResult = searchParameterResult.Any() ? searchParameterResult.IntersectBy(idResults, sp => sp, new SearchParameterInfoEqualityComparer()) : idResults;
            }

            if (!searchParameterResult.Any())
            {
                searchParameterResult = _searchParameterDefinitionManager.AllSearchParameters;
            }

            return await GetSearchParameterState((ICollection<SearchParameterInfo>)searchParameterResult, cancellationToken);
        }

        private async Task<SearchParameterStateResponse> GetSearchParameterState(ICollection<SearchParameterInfo> searchParameterResult, CancellationToken cancellationToken = default)
        {
            if (searchParameterResult.Count == 0)
            {
                return null;
            }

            SearchParameterStateResponse response;
            Parameters parameters = new Parameters();
            var states = await _searchParameterStatusManager.GetSearchParameterStatusUpdates(cancellationToken);
            foreach (var searchParam in searchParameterResult)
            {
                var parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Url,
                        Value = new FhirUrl(searchParam.Url),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirUrl(states.Where(s => s.Uri.Equals(searchParam.Url)).First().Status.ToString()),
                    },
                };
                parameters.Parameter.Add(new Parameters.ParameterComponent()
                {
                    Name = SearchParameterStateProperties.Name,
                    Part = parts,
                });
            }

            response = new SearchParameterStateResponse(parameters.ToResourceElement());
            return response;
        }
    }
}
