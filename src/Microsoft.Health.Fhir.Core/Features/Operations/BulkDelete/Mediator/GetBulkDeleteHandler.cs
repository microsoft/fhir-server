// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class GetBulkDeleteHandler : IRequestHandler<GetBulkDeleteRequest, GetBulkDeleteResponse>
    {
        public GetBulkDeleteHandler() { }

        public async Task<GetBulkDeleteResponse> Handle(GetBulkDeleteRequest request, CancellationToken cancellationToken) { }
    }
}
