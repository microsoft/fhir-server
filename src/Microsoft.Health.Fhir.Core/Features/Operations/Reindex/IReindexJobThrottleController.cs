// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public interface IReindexJobThrottleController
    {
        void Initialize(ReindexJobRecord reindexJobRecord);

        int GetThrottleBasedDelay();
    }
}
