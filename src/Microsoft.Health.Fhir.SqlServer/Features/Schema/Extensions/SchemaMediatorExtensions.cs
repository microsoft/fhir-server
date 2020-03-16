// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Messages.Get;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Extensions
{
    public static class SchemaMediatorExtensions
    {
        public static async Task<GetCompatibilityVersionResponse> GetCompatibleVersionAsync(this IMediator mediator, int minVersion, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetCompatibilityVersionRequest(minVersion);

            GetCompatibilityVersionResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }
    }
}
