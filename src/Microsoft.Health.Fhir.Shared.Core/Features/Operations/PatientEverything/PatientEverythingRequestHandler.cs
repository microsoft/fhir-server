// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Operation.PatientEverything;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.PatientEverything
{
    public class PatientEverythingRequestHandler : IRequestHandler<GetPatientEverythingRequest, GetPatientEverythingResponse>
    {
        private readonly IFhirRequestContextAccessor _requestContextAccessor;
        private readonly ISearchService _searchService;
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;
        private readonly IBundleFactory _bundleFactory;

        public PatientEverythingRequestHandler(
            IFhirRequestContextAccessor requestContextAccessor,
            ISearchService searchService,
            IReferenceSearchValueParser referenceSearchValueParser,
            IBundleFactory bundleFactory)
        {
            EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));

            _requestContextAccessor = requestContextAccessor;
            _searchService = searchService;
            _referenceSearchValueParser = referenceSearchValueParser;
            _bundleFactory = bundleFactory;
        }

        public async Task<GetPatientEverythingResponse> Handle(GetPatientEverythingRequest request, CancellationToken cancellationToken)
        {
            var reference = _referenceSearchValueParser.Parse(_requestContextAccessor.FhirRequestContext.Uri.ToString());

            if (!string.Equals(KnownResourceTypes.Patient, reference.ResourceType, StringComparison.Ordinal))
            {
                throw new SearchOperationNotSupportedException("$everything is only supported on Patient instance endpoints.");
            }

            var result = await _searchService.SearchCompartmentAsync(
                KnownResourceTypes.Patient,
                reference.ResourceId,
                null,
                _requestContextAccessor.FhirRequestContext.QueryParameters,
                cancellationToken,
                true);

            var bundle = _bundleFactory.CreateSearchBundle(result);

            return new GetPatientEverythingResponse(bundle);
        }
    }
}
