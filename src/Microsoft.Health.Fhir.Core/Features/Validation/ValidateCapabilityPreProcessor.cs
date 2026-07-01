// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Generic pipeline behavior that validates requests requiring specific capabilities.
    /// Converted from IRequestPreProcessor to IPipelineBehavior in response to Medino API changes.
    /// </summary>
    /// <typeparam name="TRequest">The type of request being validated.</typeparam>
    /// <typeparam name="TResponse">The type of response returned by the handler.</typeparam>
    public class ValidateCapabilityPreProcessor<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : class
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ValidateCapabilityPreProcessor(IConformanceProvider conformanceProvider)
        {
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));
            _conformanceProvider = conformanceProvider;
        }

        public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            if (request is IRequireCapability provider)
            {
                if (!await _conformanceProvider.SatisfiesAsync(provider.RequiredCapabilities().ToList(), cancellationToken))
                {
                    throw new MethodNotAllowedException(Core.Resources.RequestedActionNotAllowed);
                }
            }

            return await next();
        }
    }
}
