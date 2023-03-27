// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    public class SearchParameterController
    {
        private ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IMediator _mediator;
        private readonly CoreFeatureConfiguration _coreFeaturesConfig;

        public SearchParameterController(IMediator mediator, ISearchParameterDefinitionManager searchParameterDefinitionManager, IOptions<CoreFeatureConfiguration> coreFeatures)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));

            _mediator = mediator;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _coreFeaturesConfig = coreFeatures.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.SearchParameters + KnownRoutes.SearchParameterStatus)]
        [AuditEventType(AuditEventSubType.Search)]
        public async Task<IActionResult> GetSearchParameters(CancellationToken cancellationToken)
        {
            CheckIfSearchParameterStatusIsEnabledAndRespond();

            // var searchParameterInfoList = new SearchParameterInfoList();

            // IEnumerable<SearchParameterInfo> searchParameterInfos = _searchParameterDefinitionManager.AllSearchParameters
            //    .OrderBy(p => p.Name);

            // foreach (SearchParameterInfo searchParameterInfo in searchParameterInfos)
            // {
            //    searchParameterInfo.IsSupported = _searchParameterSupportResolver.IsSearchParameterSupported(searchParameterInfo.Url);

            // searchParameterInfoList.Add(searchParameterInfo);
            // }
            SearchParameterStateResponse result = await _mediator.Send(new SearchParameterStateRequest(string.Empty, string.Empty), cancellationToken);

            return FhirResult.Create(result.Bundle);
        }

        [HttpGet]
        [Route(KnownRoutes.SearchParametersResourceByType)]
        [AuditEventType(AuditEventSubType.Search)]
        public IActionResult GetSearchParametersForResourceType(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            CheckIfSearchParameterStatusIsEnabledAndRespond();

            // var searchParameterInfoList = new SearchParameterInfoList();

            // IEnumerable<SearchParameterInfo> searchParameterInfos = _searchParameterDefinitionManager.GetSearchParameters(resourceType)
            //    .Select(p => new SearchParameterInfo(p))
            //    .OrderBy(p => p.Name);

            // foreach (SearchParameterInfo searchParameterInfo in searchParameterInfos)
            // {
            //    //searchParameterInfo.IsSupported = _searchParameterSupportResolver.IsSearchParameterSupported(searchParameterInfo.Url
            // }
            return null;
        }

        /// <summary>
        /// Provide appropriate response if Search Parameter Status is not enabled
        /// </summary>
        private void CheckIfSearchParameterStatusIsEnabledAndRespond()
        {
            if (!_coreFeaturesConfig.SupportsSelectiveSearchParameters)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.SearchParameterStatus));
            }

            return;
        }
    }
}
