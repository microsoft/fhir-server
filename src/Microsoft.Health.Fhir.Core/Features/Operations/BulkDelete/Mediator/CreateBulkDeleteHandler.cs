// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class CreateBulkDeleteHandler : IRequestHandler<CreateBulkDeleteRequest, CreateBulkDeleteResponse>
    {
        public CreateBulkDeleteHandler() { }

        public async Task<CreateBulkDeleteResponse> Handle(CreateBulkDeleteRequest request, CancellationToken cancellationToken) { }
    }
}
