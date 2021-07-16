// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.GraphQl;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class GraphQlMediatorExtensions
    {
        public static async Task<GraphQlResponse> GetStatusAsync(this IMediator mediator, string resourceType, IReadOnlyList<Tuple<string, string>> queries, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(queries, nameof(queries));

            var request = new GraphQlRequest(resourceType, queries);

            GraphQlResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }
    }
}
