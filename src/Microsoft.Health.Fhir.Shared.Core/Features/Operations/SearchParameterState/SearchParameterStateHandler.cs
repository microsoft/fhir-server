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
            if (request.Queries.Count == 0)
            {
                searchParameterResult = _searchParameterDefinitionManager.AllSearchParameters;
            }
            else
            {
                List<IEnumerable<SearchParameterInfo>> results = new List<IEnumerable<SearchParameterInfo>>();
                foreach (Tuple<string, string> query in request.Queries)
                {
                    string[] queryValues = query.Item2.Split(',');

                    switch (query.Item1.ToLowerInvariant())
                    {
                        case SearchParameterStateProperties.ResourceType:
                            results.Add(_searchParameterDefinitionManager.GetSearchParametersByResourceTypes(queryValues).DefaultIfEmpty());
                            break;
                        case SearchParameterStateProperties.Code:
                            results.Add(_searchParameterDefinitionManager.GetSearchParametersByCodes(queryValues).DefaultIfEmpty());
                            break;
                        case SearchParameterStateProperties.Url:
                            results.Add(_searchParameterDefinitionManager.GetSearchParametersByUrls(queryValues).DefaultIfEmpty());
                            break;
                        default:
                            break;
                    }
                }

                searchParameterResult = IntersectAllIfEmpty(results);
            }

            return await GetSearchParameterStateAsync(searchParameterResult.ToList(), cancellationToken);
        }

        private async Task<SearchParameterStateResponse> GetSearchParameterStateAsync(ICollection<SearchParameterInfo> searchParameterResult, CancellationToken cancellationToken = default)
        {
            if (searchParameterResult.Count == 0)
            {
                return null;
            }

            SearchParameterStateResponse response;
            Parameters parameters = new Parameters();
            IReadOnlyCollection<ResourceSearchParameterStatus> states = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            foreach (SearchParameterInfo searchParam in searchParameterResult)
            {
                try
                {
                    bool hasParamStatus = states.Any(s => s.Uri.Equals(searchParam.Url));

                    List<ParameterComponent> parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Url,
                        Value = new FhirUrl(searchParam.Url),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(hasParamStatus ? states.Where(s => s.Uri.Equals(searchParam.Url)).First().Status.ToString() : SearchParameterStatus.Disabled.ToString()),
                    },
                };
                    parameters.Parameter.Add(new Parameters.ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Name,
                        Part = parts,
                    });
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            response = new SearchParameterStateResponse(parameters.ToResourceElement());
            return response;
        }

        private static IEnumerable<SearchParameterInfo> IntersectAllIfEmpty(List<IEnumerable<SearchParameterInfo>> results)
        {
            IEnumerable<SearchParameterInfo> intersectedResults = null;

            results = results.Where(l => l.Any()).ToList();

            if (results.Count > 0)
            {
                intersectedResults = results.First();

                for (int i = 1; i < results.Count; i++)
                {
                    intersectedResults = intersectedResults.IntersectBy(results.ElementAt(i), searchParameter => searchParameter, new SearchParameterInfoEqualityComparer());
                }
            }

            return intersectedResults;
        }
    }
}
