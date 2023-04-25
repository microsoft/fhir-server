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
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    /// <summary>
    /// Intercepts create/update requests and checks presence of "X-Provenance" header.
    /// If header present it proceed normal work with target request and then create provenance object with provenance.target equal to that object.
    /// </summary>
    public sealed class ProvenanceHeaderBehavior :
        IPipelineBehavior<CreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<UpsertResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>,
        IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMediator _mediator;
        private IProvenanceHeaderState _state;

        public ProvenanceHeaderBehavior(FhirJsonParser fhirJsonParser, IHttpContextAccessor httpContextAccessor, IMediator mediator, IProvenanceHeaderState state)
        {
            EnsureArg.IsNotNull(fhirJsonParser, nameof(fhirJsonParser));
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _fhirJsonParser = fhirJsonParser;
            _httpContextAccessor = httpContextAccessor;
            _mediator = mediator;
            _state = state;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(next, cancellationToken);

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(next, cancellationToken);

        public async Task<UpsertResourceResponse> Handle(UpsertResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(next, cancellationToken);

        public async Task<UpsertResourceResponse> Handle(CreateResourceRequest request, RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
            => await GenericHandle(next, cancellationToken);

        private async Task<UpsertResourceResponse> GenericHandle(RequestHandlerDelegate<UpsertResourceResponse> next, CancellationToken cancellationToken)
        {
            if (_state.Intercepted)
            {
                return await next();
            }

            _state.Intercepted = true;
            Provenance provenance = GetProvenanceFromHeader();
            var response = await next();

            if (response != null && provenance != null)
            {
                // Set target to provided resource.
                provenance.Target = new System.Collections.Generic.List<ResourceReference>()
                {
                    new ResourceReference($"{response.Outcome.RawResourceElement.InstanceType}/{response.Outcome.RawResourceElement.Id}/_history/{response.Outcome.RawResourceElement.VersionId}"),
                };

                // Create Provenance resource.
                // TODO: It should probaby go through controller to trigger audit events, but it's quite tricky to do now.
                await _mediator.Send<UpsertResourceResponse>(new CreateResourceRequest(provenance.ToResourceElement(), bundleOperationId: null), cancellationToken);
            }

            return response;
        }

        private Provenance GetProvenanceFromHeader()
        {
            if (!_httpContextAccessor.HttpContext.Request.Headers.ContainsKey(KnownHeaders.ProvenanceHeader))
            {
                return null;
            }

            Provenance provenance;
            try
            {
                provenance = _fhirJsonParser.Parse<Provenance>(_httpContextAccessor.HttpContext.Request.Headers[KnownHeaders.ProvenanceHeader]);
            }
            catch
            {
                throw new ProvenanceHeaderException(Core.Resources.ProvenanceHeaderMalformed);
            }

            if (provenance.Target != null && provenance.Target.Count > 0)
            {
                throw new ProvenanceHeaderException(Core.Resources.ProvenanceHeaderShouldntHaveTarget);
            }

            return provenance;
        }
    }
}
