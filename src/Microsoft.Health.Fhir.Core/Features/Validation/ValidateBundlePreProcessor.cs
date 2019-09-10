// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using MediatR.Pipeline;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateBundlePreProcessor : IRequestPreProcessor<BundleRequest>
    {
        public Task Process(BundleRequest request, CancellationToken cancellationToken)
        {
            if (request.Bundle.InstanceType != KnownResourceTypes.Bundle)
            {
                throw new RequestNotValidException(Core.Resources.BundleRequiredForBatchOrTransaction);
            }

            return Task.CompletedTask;
        }
    }
}
