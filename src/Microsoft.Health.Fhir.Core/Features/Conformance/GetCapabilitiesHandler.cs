// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Get;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetCapabilitiesHandler : IRequestHandler<GetCapabilitiesRequest, GetCapabilitiesResponse>
    {
        private readonly IConformanceProvider _provider;
        private readonly IUrlResolver _urlResolver;

        public GetCapabilitiesHandler(IConformanceProvider provider, IUrlResolver urlResolver)
        {
            EnsureArg.IsNotNull(provider, nameof(provider));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));

            _provider = provider;
            _urlResolver = urlResolver;
        }

        public async Task<GetCapabilitiesResponse> Handle(GetCapabilitiesRequest message, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(message, nameof(message));

            var response = await _provider.GetCapabilityStatementAsync(cancellationToken);

            return new GetCapabilitiesResponse(response);
        }
    }
}
