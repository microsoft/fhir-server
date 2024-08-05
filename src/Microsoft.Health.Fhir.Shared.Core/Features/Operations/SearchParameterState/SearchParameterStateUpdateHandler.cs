﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Audit;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Audit;
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
        private readonly ILogger<SearchParameterStateUpdateHandler> _logger;
        private readonly IAuditLogger _auditLogger;

        public SearchParameterStateUpdateHandler(IAuthorizationService<DataActions> authorizationService, SearchParameterStatusManager searchParameterStatusManager, ILogger<SearchParameterStateUpdateHandler> logger, IAuditLogger auditLogger)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(auditLogger, nameof(auditLogger));

            _authorizationService = authorizationService;
            _searchParameterStatusManager = searchParameterStatusManager;
            _logger = logger;
            _auditLogger = auditLogger;
        }

        public async Task<SearchParameterStateUpdateResponse> Handle(SearchParameterStateUpdateRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.SearchParameter, cancellationToken) != DataActions.SearchParameter)
            {
                throw new UnauthorizedFhirActionException();
            }

            _resourceSearchParameterStatus = await _searchParameterStatusManager.GetAllSearchParameterStatus(cancellationToken);
            Dictionary<SearchParameterStatus, List<string>> searchParametersToUpdate = ParseRequestForUpdate(request, out List<OperationOutcomeIssue> invalidSearchParameters);
            foreach (var statusGroup in searchParametersToUpdate)
            {
                await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(statusGroup.Value, statusGroup.Key, cancellationToken);
            }

            return CreateBundleResponse(searchParametersToUpdate, invalidSearchParameters);
        }

        private Dictionary<SearchParameterStatus, List<string>> ParseRequestForUpdate(SearchParameterStateUpdateRequest request, out List<OperationOutcomeIssue> invalidSearchParameters)
        {
            var searchParametersToUpdate = new Dictionary<SearchParameterStatus, List<string>>();
            invalidSearchParameters = new List<OperationOutcomeIssue>();

            foreach (var searchParameter in request.SearchParameters)
            {
                System.Uri uri = searchParameter.Item1;
                SearchParameterStatus status = searchParameter.Item2;
                ResourceSearchParameterStatus searchParameterInfo = _resourceSearchParameterStatus.FirstOrDefault(sp => sp.Uri.Equals(uri));
                if (searchParameterInfo == null)
                {
                    invalidSearchParameters.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Information,
                        OperationOutcomeConstants.IssueType.NotFound,
                        detailsText: string.Format(Core.Resources.SearchParameterNotFound, uri)));
                }
                else if (!(status.Equals(SearchParameterStatus.Supported) || status.Equals(SearchParameterStatus.Disabled)))
                {
                    invalidSearchParameters.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.Invalid,
                        detailsText: string.Format(Core.Resources.InvalidUpdateStatus, status, uri)));
                }
                else if (searchParameterInfo.Status.Equals(SearchParameterStatus.Deleted) || searchParameterInfo.Status.Equals(SearchParameterStatus.Unsupported))
                {
                    invalidSearchParameters.Add(new OperationOutcomeIssue(
                        OperationOutcomeConstants.IssueSeverity.Error,
                        OperationOutcomeConstants.IssueType.NotSupported,
                        detailsText: string.Format(Core.Resources.SearchParameterDeleted, uri)));
                }
                else
                {
                    SearchParameterStatus statusValue = status == SearchParameterStatus.Disabled ? SearchParameterStatus.PendingDisable : status;
                    if (searchParametersToUpdate.TryGetValue(statusValue, out List<string> value))
                    {
                        value.Add(uri.ToString());
                    }
                    else
                    {
                        searchParametersToUpdate.Add(statusValue, new List<string>() { uri.ToString() });
                    }
                }
            }

            return searchParametersToUpdate;
        }

        private SearchParameterStateUpdateResponse CreateBundleResponse(Dictionary<SearchParameterStatus, List<string>> searchParametersToUpdate, List<OperationOutcomeIssue> invalidSearchParameters)
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

                _logger.LogInformation("The following search parameters were not updated: {Issue}", string.Join(", ", invalidSearchParameters.Select(x => x.DetailsText)));
            }

            if (searchParametersToUpdate.Any())
            {
                Hl7.Fhir.Model.Parameters succeededResults = new Hl7.Fhir.Model.Parameters();

                foreach (var searchParameterGroup in searchParametersToUpdate)
                {
                    foreach (string uri in searchParameterGroup.Value)
                    {
                        List<ParameterComponent> parts = new List<ParameterComponent>
                        {
                            new ParameterComponent()
                            {
                                Name = SearchParameterStateProperties.Url,
                                Value = new FhirUrl(uri),
                            },
                            new ParameterComponent()
                            {
                                Name = SearchParameterStateProperties.Status,
                                Value = new FhirString(searchParameterGroup.Key.ToString()),
                            },
                        };
                        succeededResults.Parameter.Add(new Hl7.Fhir.Model.Parameters.ParameterComponent()
                        {
                            Name = SearchParameterStateProperties.Name,
                            Part = parts,
                        });

                        _logger.LogInformation("SearchParameterUpdated. SearchParameter: {SearchParameter}, Status: {Status}", uri, searchParameterGroup.Key.ToString().ToLowerInvariant());
                        _auditLogger.LogAudit(
                            AuditAction.Executed,
                            OperationsConstants.SearchParameterStatus,
                            ResourceType.SearchParameter.ToString(),
                            new Uri(uri),
                            System.Net.HttpStatusCode.OK,
                            null,
                            null,
                            null,
                            additionalProperties: new Dictionary<string, string>()
                            {
                                { "Status", searchParameterGroup.Key.ToString() },
                            });
                    }
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
