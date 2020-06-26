// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;

namespace Microsoft.Health.CosmosDb.UnitTests.Features.Health
{
    internal class TestCosmosHealthCheck : CosmosHealthCheck
    {
        public TestCosmosHealthCheck(
            IScoped<Container> documentClient,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            ICosmosClientTestProvider testProvider,
            ILogger<CosmosHealthCheck> logger)
            : base(
                  documentClient,
                  configuration,
                  namedCosmosCollectionConfigurationAccessor,
                  testProvider,
                  logger)
        {
        }
    }
}
