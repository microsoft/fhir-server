// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Pipeline behavior that validates Bundle requests to ensure the request contains a valid Bundle resource.
    /// Converted from IRequestPreProcessor to IPipelineBehavior in response to Medino API changes.
    /// </summary>
    public class ValidateBundlePreProcessor : IPipelineBehavior<BundleRequest, BundleResponse>
    {
        public async Task<BundleResponse> HandleAsync(BundleRequest request, RequestHandlerDelegate<BundleResponse> next, CancellationToken cancellationToken)
        {
            if (request.Bundle.InstanceType != KnownResourceTypes.Bundle)
            {
                throw new RequestNotValidException(Core.Resources.BundleRequiredForBatchOrTransaction);
            }

            return await next();
        }
    }
}
