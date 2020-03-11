// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Schema;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class SchemaMediatorExtensions
    {
        public static async Task<GetCompatibilityVersionResponse> GetCompatibleVersionAsync(this IMediator mediator, int minVersion, int maxVersion, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetCompatibilityVersionRequest(minVersion, maxVersion);

            GetCompatibilityVersionResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }
    }
}
