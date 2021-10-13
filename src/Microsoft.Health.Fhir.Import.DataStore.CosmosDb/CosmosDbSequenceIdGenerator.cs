// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Import.Core;
using Microsoft.Health.Fhir.Import.Core.Test;

namespace Microsoft.Health.Fhir.Import.DataStore.CosmosDb
{
    public class CosmosDbSequenceIdGenerator : ISequenceIdGenerator<long>
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
