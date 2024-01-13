﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.Delete;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public interface IDeletionService
    {
        public Task<ResourceKey> DeleteAsync(DeleteResourceRequest request, CancellationToken cancellationToken);

        public Task<long> DeleteMultipleAsync(ConditionalDeleteResourceRequest request, CancellationToken cancellationToken);
    }
}
