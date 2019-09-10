// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Bundle;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Resources.Bundle
{
    public class BundleHandler : IRequestHandler<BundleRequest, BundleResponse>
    {
        public Task<BundleResponse> Handle(BundleRequest request, CancellationToken cancellationToken)
        {
            throw new System.NotImplementedException();
        }
    }
}
