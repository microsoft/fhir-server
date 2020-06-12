// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.CosmosDb.Features.Storage
{
    public class CosmosDbDistributedLockFactory : ICosmosDbDistributedLockFactory
    {
        private readonly Func<IScoped<Container>> _documentClientFactory;
        private readonly ILogger<CosmosDbDistributedLock> _logger;

        public CosmosDbDistributedLockFactory(Func<IScoped<Container>> documentClientFactory, ILogger<CosmosDbDistributedLock> logger)
        {
            EnsureArg.IsNotNull(documentClientFactory, nameof(documentClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _documentClientFactory = documentClientFactory;
            _logger = logger;
        }

        public ICosmosDbDistributedLock Create(string lockId)
        {
            EnsureArg.IsNotNullOrEmpty(lockId, nameof(lockId));

            return new CosmosDbDistributedLock(_documentClientFactory, lockId, _logger);
        }

        public ICosmosDbDistributedLock Create(Container client, string lockId)
        {
            EnsureArg.IsNotNullOrEmpty(lockId, nameof(lockId));

            return new CosmosDbDistributedLock(() => new NonDisposingScope(client), lockId, _logger);
        }
    }
}
