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
using Medino;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Generic pipeline behavior that validates requests using FluentValidation validators.
    /// Converted from IRequestPreProcessor to IPipelineBehavior in response to Medino API changes.
    /// </summary>
    /// <typeparam name="TRequest">The type of request being validated.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
    public class ValidateRequestPreProcessor<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : class
    {
        private readonly IEnumerable<IValidator<TRequest>> _validators;

        public ValidateRequestPreProcessor(IEnumerable<IValidator<TRequest>> validators)
        {
            EnsureArg.IsNotNull(validators, nameof(validators));
            _validators = validators;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var allResults = (await Task.WhenAll(_validators.Select(x => x.ValidateAsync(request, cancellationToken)))).Where(x => x != null).ToArray();

            if (!allResults.All(x => x.IsValid))
            {
                throw new ResourceNotValidException(allResults.SelectMany(x => x.Errors).ToList());
            }

            return await next();
        }
    }
}
