// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    public class ValidateCapabilityPreProcessor<TRequest> : IRequestPreProcessor<TRequest>
    {
        private readonly IConformanceProvider _conformanceProvider;

        public ValidateCapabilityPreProcessor(IConformanceProvider conformanceProvider)
        {
            EnsureArg.IsNotNull(conformanceProvider, nameof(conformanceProvider));

            _conformanceProvider = conformanceProvider;
        }

        public async Task Process(TRequest request, CancellationToken cancellationToken)
        {
            if (request is IRequireCapability provider)
            {
                if (!await _conformanceProvider.SatisfiesAsync(provider.RequiredCapabilities(), cancellationToken))
                {
                    throw new MethodNotAllowedException(Core.Resources.RequestedActionNotAllowed);
                }
            }
        }
    }
}
