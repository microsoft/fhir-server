// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateBundlePreProcessor : IRequestPreProcessor<BundleRequest>
    {
        private readonly IResourceValidator _resourceValidator;

        public ValidateBundlePreProcessor(IResourceValidator resourceValidator)
        {
            EnsureArg.IsNotNull(resourceValidator, nameof(resourceValidator));
            _resourceValidator = resourceValidator;
        }

        public Task Process(BundleRequest request, CancellationToken cancellationToken)
        {
            if (request.Bundle.InstanceType != KnownResourceTypes.Bundle)
            {
                throw new RequestNotValidException(Core.Resources.BundleRequiredForBatchOrTransaction);
            }

            var results = _resourceValidator.TryValidate(request.Bundle.Instance);
            if (results.Length != 0)
            {
                throw new ResourceNotValidException(results);
            }

            return Task.CompletedTask;
        }
    }
}
