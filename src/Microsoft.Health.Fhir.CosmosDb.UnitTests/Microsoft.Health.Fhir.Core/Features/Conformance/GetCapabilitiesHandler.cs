// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Get;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class GetCapabilitiesHandler : IRequestHandler<GetCapabilitiesRequest, GetCapabilitiesResponse>
    {
        private readonly IConformanceProvider _provider;

        public GetCapabilitiesHandler(IConformanceProvider provider)
        {
            EnsureArg.IsNotNull(provider, nameof(provider));

            _provider = provider;
        }

        public async Task<GetCapabilitiesResponse> Handle(GetCapabilitiesRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var response = await _provider.GetMetadata(cancellationToken);

            return new GetCapabilitiesResponse(response);
        }
    }
}
