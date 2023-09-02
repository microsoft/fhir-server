// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages
{
    public class CreateBulkDeleteResponse
    {
        public CreateBulkDeleteResponse(long id)
        {
            Id = id;
        }

        public long Id { get; }
    }
}
