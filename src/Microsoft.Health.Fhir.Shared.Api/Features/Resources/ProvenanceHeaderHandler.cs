// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.ProvenanceHeader;
using Microsoft.Health.Fhir.Core.Messages.Upsert;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    /// <summary>
    /// Handler for request with "X-Provenance header".
    /// Creates resource and Provenance resource provided in "X-Provenance" header with it's target as resource.
    /// </summary>
    public sealed class ProvenanceHeaderHandler : IRequestHandler<ProvenanceHeaderRequest, RawResourceElement>
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IMediator _mediator;

        public ProvenanceHeaderHandler(FhirJsonParser fhirJsonParser, IMediator mediator)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _fhirJsonParser = fhirJsonParser;
            _mediator = mediator;
        }

        public async Task<RawResourceElement> Handle(ProvenanceHeaderRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            Provenance provenance;
            try
            {
                provenance = _fhirJsonParser.Parse<Provenance>(request.ProvenanceHeader);
            }
            catch
            {
                throw new ProvenanceHeaderException(Core.Resources.ProvenanceHeaderMalformed);
            }

            if (provenance.Target != null && provenance.Target.Count > 0)
            {
                throw new ProvenanceHeaderException(Core.Resources.ProvenanceHeaderShouldntHaveTarget);
            }

            // Create target resource first.
            UpsertResourceResponse targetResource = await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(request.Target), cancellationToken);

            // Set target to provided resource.
            provenance.Target = new System.Collections.Generic.List<ResourceReference>()
            {
                new ResourceReference($"{targetResource.Outcome.RawResourceElement.InstanceType}/{targetResource.Outcome.RawResourceElement.Id}"),
            };

            // Create Provenance resource.
            await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(provenance.ToResourceElement()), cancellationToken);
            return targetResource.Outcome.RawResourceElement;
        }
    }
}
