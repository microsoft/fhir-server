// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Operation;

namespace Microsoft.Health.Fhir.Shared.Core.Extensions
{
    public static class OperationDefinitionMediatorExtensions
    {
        public static async Task<OperationDefinitionResponse> GetOperationDefinitionAsync(this IMediator mediator, string operationName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(operationName, nameof(operationName));

            return await mediator.Send(new OperationDefinitionRequest(operationName), cancellationToken);
        }
    }
}
