// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Search Parameter State Controller
    /// </summary>
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class SearchParameterController : Controller
    {
        private readonly IMediator _mediator;
        private readonly CoreFeatureConfiguration _coreFeaturesConfig;

        public SearchParameterController(IMediator mediator, IOptions<CoreFeatureConfiguration> coreFeatures)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));

            _mediator = mediator;
            _coreFeaturesConfig = coreFeatures.Value;
        }

        /// <summary>
        /// Gets all search parameters with current status.
        /// </summary>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Parameters resource containing search parameters matching search query. Returns all search parameters with current state if no search query provided. Along with Status for each result.</returns>
        [HttpGet]
        [Route(KnownRoutes.SearchParametersStatusQuery, Name = RouteNames.SearchParameterState)]
        [AuditEventType(AuditEventSubType.SearchParameterStatus)]
        [ValidateSearchParameterStateRequestAtrribute]
        public async Task<IActionResult> GetSearchParametersStatus(CancellationToken cancellationToken)
        {
            CheckIfSearchParameterStatusIsEnabledAndRespond();

            SearchParameterStateRequest request = new SearchParameterStateRequest(GetQueriesForSearch());
            SearchParameterStateResponse result = await _mediator.Send(request, cancellationToken);

            _ = result ?? throw new ResourceNotFoundException(Resources.SearchParameterStatusNotFound);

            return FhirResult.Create(result.SearchParameters, System.Net.HttpStatusCode.OK);
        }

        /// <summary>
        /// Search for multiple Search Parameter Status using POST.
        /// </summary>
        /// <param name="inputParams">Parameters resource containing the search parameters needing a status looked up.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <returns>Parameters resource containing Search Parameters with Status.</returns>
        [HttpPost]
        [Route(KnownRoutes.SearchParameters + KnownRoutes.SearchParametersStatusPostQuery, Name = RouteNames.PostSearchParameterState)]
        [ValidateParametersResource]
        [AuditEventType(AuditEventSubType.SearchParameterStatus)]
        public async Task<IActionResult> PostSearchParametersStatus(CancellationToken cancellationToken)
        {
            CheckIfSearchParameterStatusIsEnabledAndRespond();

            SearchParameterStateRequest request = new SearchParameterStateRequest(GetQueriesForSearch());
            SearchParameterStateResponse result = await _mediator.Send(request, cancellationToken);

            _ = result ?? throw new ResourceNotFoundException(Resources.SearchParameterStatusNotFound);

            return FhirResult.Create(result.SearchParameters, System.Net.HttpStatusCode.OK);
        }

        /// <summary>
        /// Updates SearchParameters to either supported or disabled state.
        /// </summary>
        /// <param name="inputParams">SearchParameters to update the status of.</param>
        /// <param name="cancellationToken">Cancellation Token.</param>
        /// <returns>Returns operation outcome with the updates status of each search parameter in request.</returns>
        [HttpPut]
        [Route(KnownRoutes.SearchParametersStatusQuery, Name = RouteNames.UpdateSearchParameterState)]
        [AuditEventType(AuditEventSubType.SearchParameterStatus)]
        public async Task<IActionResult> UpdateSearchParametersStatus([FromBody] Parameters inputParams, CancellationToken cancellationToken)
        {
            CheckIfSearchParameterStatusIsEnabledAndRespond();
            SearchParameterStateUpdateRequest updateRequest = ParseUpdateRequestBody(inputParams);
            SearchParameterStateUpdateResponse result = await _mediator.Send<SearchParameterStateUpdateResponse>(updateRequest, cancellationToken);

            _ = result ?? throw new ResourceNotFoundException(Resources.SearchParameterStatusNotFound);
            return FhirResult.Create(result.UpdateStatus, System.Net.HttpStatusCode.OK);
        }

        /// <summary>
        /// Provide appropriate response if Search Parameter Status feature is not enabled
        /// </summary>
        private void CheckIfSearchParameterStatusIsEnabledAndRespond()
        {
            if (!_coreFeaturesConfig.SupportsSelectiveSearchParameters)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.SearchParameterStatus));
            }
        }

        /// <summary>
        /// Parses the query string parameters for search.
        /// </summary>
        /// <returns>The query string parameters for search from the request object.</returns>
        private IReadOnlyList<Tuple<string, string>> GetQueriesForSearch()
        {
            return Request.GetQueriesForSearch();
        }

        private static SearchParameterStateUpdateRequest ParseUpdateRequestBody(Parameters inputParams)
        {
            List<Tuple<Uri, SearchParameterStatus>> paramsToUpdate = new List<Tuple<Uri, SearchParameterStatus>>();

            foreach (var parameter in inputParams.Parameter)
            {
                var url = parameter.Part.Find(p => p.Name.Equals(SearchParameterStateProperties.Url, StringComparison.OrdinalIgnoreCase))?.Value.ToString();
                var stringStatus = parameter.Part.Find(p => p.Name.Equals(SearchParameterStateProperties.Status, StringComparison.OrdinalIgnoreCase))?.Value.ToString();
                var isValidStatus = Enum.TryParse(stringStatus, out SearchParameterStatus status);

                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(stringStatus))
                {
                    throw new RequestNotValidException(Core.Resources.SearchParameterRequestNotValid);
                }
                else if (isValidStatus)
                {
                    paramsToUpdate.Add(new Tuple<Uri, SearchParameterStatus>(new Uri(url), status));
                }
                else
                {
                    throw new RequestNotValidException(string.Format(Core.Resources.SearchParameterStatusNotValid, stringStatus, url));
                }
            }

            return new SearchParameterStateUpdateRequest(paramsToUpdate);
        }
    }
}
