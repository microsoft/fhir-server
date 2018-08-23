// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosDbDistributedLockFactory : ICosmosDbDistributedLockFactory
    {
        private readonly ILogger<CosmosDbDistributedLock> _logger;

        public CosmosDbDistributedLockFactory(ILogger<CosmosDbDistributedLock> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        public ICosmosDbDistributedLock Create(IDocumentClient client, Uri collectionUri, string lockId)
        {
            EnsureArg.IsNotNull(collectionUri, nameof(collectionUri));
            EnsureArg.IsNotNull(collectionUri, nameof(collectionUri));
            EnsureArg.IsNotNullOrEmpty(lockId, nameof(lockId));

            return new CosmosDbDistributedLock(() => client, collectionUri, lockId, _logger);
        }
    }
}
