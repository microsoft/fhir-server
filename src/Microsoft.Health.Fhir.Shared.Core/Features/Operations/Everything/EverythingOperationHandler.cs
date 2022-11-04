// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    public class EverythingOperationHandler : IRequestHandler<EverythingOperationRequest, EverythingOperationResponse>
    {
        private readonly IPatientEverythingService _patientEverythingService;
        private readonly IBundleFactory _bundleFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IDataResourceFilter _dataResourceFilter;

        public EverythingOperationHandler(IPatientEverythingService patientEverythingService, IBundleFactory bundleFactory, IAuthorizationService<DataActions> authorizationService, IDataResourceFilter dataResourceFilter)
        {
            EnsureArg.IsNotNull(patientEverythingService, nameof(patientEverythingService));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(dataResourceFilter, nameof(dataResourceFilter));

            _patientEverythingService = patientEverythingService;
            _bundleFactory = bundleFactory;
            _authorizationService = authorizationService;
            _dataResourceFilter = dataResourceFilter;
        }

        public async Task<EverythingOperationResponse> Handle(EverythingOperationRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (await _authorizationService.CheckAccess(DataActions.Read, cancellationToken) != DataActions.Read)
            {
                throw new UnauthorizedFhirActionException();
            }

            if (!string.Equals(request.EverythingOperationType, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                throw new RequestNotValidException(string.Format(Core.Resources.ResourceNotSupported, request.EverythingOperationType));
            }

            SearchResult searchResult = await _patientEverythingService.SearchAsync(
                request.ResourceId,
                request.Start,
                request.End,
                request.Since,
                request.ResourceTypes,
                request.ContinuationToken,
                cancellationToken);

            searchResult = _dataResourceFilter.Filter(searchResult: searchResult);

            ResourceElement bundle = request.UnsupportedParameters != null && request.UnsupportedParameters.Any()
                ? _bundleFactory.CreateSearchBundle(new SearchResult(searchResult.Results, searchResult.ContinuationToken, searchResult.SortOrder, request.UnsupportedParameters))
                : _bundleFactory.CreateSearchBundle(searchResult);

            return new EverythingOperationResponse(bundle);
        }
    }
}
