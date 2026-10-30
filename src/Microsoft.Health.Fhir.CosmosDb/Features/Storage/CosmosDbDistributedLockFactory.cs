// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.Scoping;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosDbDistributedLockFactory : ICosmosDbDistributedLockFactory
    {
        private readonly IScopeProvider<Container> _containerScopeFactory;
        private readonly ILogger<CosmosDbDistributedLock> _logger;

        public CosmosDbDistributedLockFactory(IScopeProvider<Container> containerScopeFactory, ILogger<CosmosDbDistributedLock> logger)
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

            return new CosmosDbDistributedLock(new ExistingContainerScopeProvider(container), lockId, _logger);
        }

        private sealed class ExistingContainerScopeProvider(Container container) : IScopeProvider<Container>
        {
            public IScoped<Container> Invoke() => new NonDisposingScope(container);
        }
    }
}
