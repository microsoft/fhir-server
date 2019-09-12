// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.Core.Features.Resources.Upsert
{
    public partial class UpsertResourceHandler : BaseResourceHandler, IRequestHandler<UpsertResourceRequest, UpsertResourceResponse>
    {
        private async Task<UpsertOutcome> UpsertAsync(UpsertResourceRequest message, ResourceWrapper resourceWrapper, bool allowCreate, bool keepHistory, CancellationToken cancellationToken)
        {
            UpsertOutcome result;

            try
            {
                result = await FhirDataStore.UpsertAsync(resourceWrapper, message.WeakETag, allowCreate, keepHistory, cancellationToken);
            }
            catch (PreconditionFailedException)
            {
                throw new ResourceConflictException(message.WeakETag);
            }

            return result;
        }
    }
}
