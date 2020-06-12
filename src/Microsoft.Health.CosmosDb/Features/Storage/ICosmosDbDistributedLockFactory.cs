// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Cosmos;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public interface ICosmosDbDistributedLockFactory
    {
        ICosmosDbDistributedLock Create(string lockId);

        ICosmosDbDistributedLock Create(Container container, string lockId);
    }
}
