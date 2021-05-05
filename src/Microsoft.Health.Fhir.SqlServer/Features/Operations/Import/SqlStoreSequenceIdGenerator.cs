// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Operations.Import
{
    public class SqlStoreSequenceIdGenerator : ISequenceIdGenerator<long>
    {
        public long GetCurrentSequenceId()
        {
            return ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.UtcNow);
        }
    }
}
