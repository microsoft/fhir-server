// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Scoping;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Health
{
    public class TestCosmosDbHealthCheck : CosmosDbHealthCheck
    {
        public TestCosmosDbHealthCheck(
            IScoped<Container> container,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosClientTestProvider testProvider,
            ILogger<CosmosDbHealthCheck> logger)
            : base(
                  container.CreateMockScopeProviderFromScoped(),
                  configuration,
                  namedCosmosCollectionConfigurationAccessor,
                  testProvider,
                  logger)
        {
        }
    }
}
