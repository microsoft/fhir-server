// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Scoping;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosDbDistributedLockFactory : ICosmosDbDistributedLockFactory
    {
        private readonly IOwnedScopeFactory<Container> _containerScopeFactory;
        private readonly ILogger<CosmosDbDistributedLock> _logger;

        public CosmosDbDistributedLockFactory(IOwnedScopeFactory<Container> containerScopeFactory, ILogger<CosmosDbDistributedLock> logger)
        {
            EnsureArg.IsNotNull(containerScopeFactory, nameof(containerScopeFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerScopeFactory = containerScopeFactory;
            _logger = logger;
        }

        public ICosmosDbDistributedLock Create(string lockId)
        {
            EnsureArg.IsNotNullOrEmpty(lockId, nameof(lockId));

            return new CosmosDbDistributedLock(_containerScopeFactory, lockId, _logger);
        }

        public ICosmosDbDistributedLock Create(Container container, string lockId)
        {
            EnsureArg.IsNotNullOrEmpty(lockId, nameof(lockId));

            return new CosmosDbDistributedLock(new ExistingContainerScopeFactory(container), lockId, _logger);
        }

        private sealed class ExistingContainerScopeFactory(Container container) : IOwnedScopeFactory<Container>
        {
            public IOwnedScope<Container> Create() => new NonDisposingScope(container);
        }
    }
}
