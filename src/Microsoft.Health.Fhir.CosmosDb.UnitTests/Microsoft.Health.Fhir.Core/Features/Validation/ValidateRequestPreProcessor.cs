// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation;
using MediatR.Pipeline;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateRequestPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
        where TRequest : class
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidateRequestPreProcessor(IEnumerable<IValidator<TRequest>> validators)
        {
            EnsureArg.IsNotNull(validators, nameof(validators));

            _validators = validators;
        }

        public async Task Process(TRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var allResults = (await Task.WhenAll(_validators.Select(x => x.ValidateAsync(request, cancellationToken)))).Where(x => x != null).ToArray();

            if (!allResults.All(x => x.IsValid))
            {
                throw new ResourceNotValidException(allResults.SelectMany(x => x.Errors).ToList());
            }
        }
    }
}
