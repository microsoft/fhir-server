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
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Parameters;

namespace Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState
{
    public class SearchParameterStateUpdateHandler : IRequestHandler<SearchParameterStateUpdateRequest, SearchParameterStateUpdateResponse>
    {
        private const string FailedSearchParameterUpdates = "FailedSearchParameterUpdates";
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private IReadOnlyCollection<ResourceSearchParameterStatus> _resourceSearchParameterStatus = null;

        public SearchParameterStateUpdateHandler(IAuthorizationService<DataActions> authorizationService, SearchParameterStatusManager searchParameterStatusManager)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));

            _authorizationService = authorizationService;
            _searchParameterStatusManager = searchParameterStatusManager;
        }

        public async Task<SearchParameterStateUpdateResponse> Handle(SearchParameterStateUpdateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            _resourceSearchParameterStatus = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            IReadOnlyCollection<ResourceSearchParameterStatus> searchParametersToUpdate = ParseRequestForUpdate(request, out List<OperationOutcomeIssue> invalidSearchParameters);
            await _searchParameterStatusManager.ApplySearchParameterStatus(searchParametersToUpdate, cancellationToken);
            return CreateBundleResponse(searchParametersToUpdate, invalidSearchParameters);
        }

        private IReadOnlyCollection<ResourceSearchParameterStatus> ParseRequestForUpdate(SearchParameterStateUpdateRequest request, out List<OperationOutcomeIssue> invalidSearchParameters)
        {
            var searchParametersToUpdate = new List<ResourceSearchParameterStatus>();
            invalidSearchParameters = new List<OperationOutcomeIssue>();

            foreach (var searchParameter in request.SearchParameters)
            {
                var uri = searchParameter.Item1;
                var status = searchParameter.Item2;
                var searchParameterInfo = _resourceSearchParameterStatus.Where(sp => sp.Uri.Equals(uri)).FirstOrDefault();
                if (searchParameterInfo == null)
                {
                    invalidSearchParameters.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(Core.Resources.SearchParameterNotFound, status, uri)));
                }
                else if (!(status.Equals(SearchParameterStatus.Supported) || status.Equals(SearchParameterStatus.Disabled)))
                {
                    invalidSearchParameters.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(Core.Resources.InvalidUpdateStatus, uri)));
                }
                else if (searchParameterInfo.Status.Equals(SearchParameterStatus.Deleted))
                {
                    invalidSearchParameters.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Invalid,
                        string.Format(Core.Resources.SearchParameterDeleted, uri)));
                }
                else
                {
                    searchParametersToUpdate.Add(new ResourceSearchParameterStatus
                    {
                        Uri = uri,
                        Status = status,
                        LastUpdated = Clock.UtcNow,
                        IsPartiallySupported = searchParameterInfo.IsPartiallySupported,
                        SortStatus = searchParameterInfo.SortStatus,
                    });
                }
            }

            return searchParametersToUpdate;
        }

        private static SearchParameterStateUpdateResponse CreateBundleResponse(IReadOnlyCollection<ResourceSearchParameterStatus> searchParametersToUpdate, List<OperationOutcomeIssue> invalidSearchParameters)
        {
            // Create the bundle from the result.
            var bundle = new Bundle();

            if (invalidSearchParameters.Any())
            {
                var operationOutcome = new OperationOutcome
                {
                    Id = FailedSearchParameterUpdates,
                    Issue = new List<OperationOutcome.IssueComponent>(invalidSearchParameters.Select(x => x.ToPoco()).ToList()),
                };

                bundle.Entry.Add(
                    new Bundle.EntryComponent
                    {
                        Resource = operationOutcome,
                    });
            }

            if (searchParametersToUpdate.Any())
            {
                Parameters succeededResults = new Parameters();

                foreach (var searchParameter in searchParametersToUpdate)
                {
                    List<ParameterComponent> parts = new List<ParameterComponent>
                    {
                        new ParameterComponent()
                        {
                            Name = SearchParameterStateProperties.Url,
                            Value = new FhirUrl(searchParameter.Uri),
                        },
                        new ParameterComponent()
                        {
                            Name = SearchParameterStateProperties.Status,
                            Value = new FhirString(searchParameter.Status.ToString()),
                        },
                    };
                    succeededResults.Parameter.Add(new Parameters.ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Name,
                        Part = parts,
                    });
                }

                bundle.Entry.Add(
                        new Bundle.EntryComponent
                        {
                            Resource = succeededResults,
                        });
            }

            bundle.Type = Bundle.BundleType.BatchResponse;
            bundle.Total = invalidSearchParameters?.Count + searchParametersToUpdate?.Count;
            bundle.Meta = new Meta
            {
                LastUpdated = Clock.UtcNow,
            };

            return new SearchParameterStateUpdateResponse(bundle.ToResourceElement());
        }
    }
}
