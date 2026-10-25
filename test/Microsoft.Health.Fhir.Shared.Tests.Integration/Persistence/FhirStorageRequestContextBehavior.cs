// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    internal sealed class FhirStorageRequestContextBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull
    {
        private readonly RequestContextAccessor<IFhirRequestContext> _accessor;

        /// <summary>
        /// Initializes the foreground request-context boundary.
        /// </summary>
        public FhirStorageRequestContextBehavior(RequestContextAccessor<IFhirRequestContext> accessor)
        {
            _accessor = accessor;
        }

        /// <inheritdoc/>
        public Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            _ = _accessor.RequestContext;
            return next();
        }
    }
}
