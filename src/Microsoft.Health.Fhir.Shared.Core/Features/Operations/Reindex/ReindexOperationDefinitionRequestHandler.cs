// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Reindex;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Operations.Reindex
{
    public class ReindexOperationDefinitionRequestHandler : IRequestHandler<ReindexOperationDefinitionRequest, ReindexOperationDefinitionResponse>
    {
        public Task<ReindexOperationDefinitionResponse> Handle(ReindexOperationDefinitionRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
