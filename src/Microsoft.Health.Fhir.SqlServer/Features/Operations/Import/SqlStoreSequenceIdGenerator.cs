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
        /// <summary>
        /// Get current surrogateId from datetime
        /// </summary>
        /// <returns>Current surrogated id.</returns>
        public long GetCurrentSequenceId()
        {
            return ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.UtcNow);
        }
    }
}
