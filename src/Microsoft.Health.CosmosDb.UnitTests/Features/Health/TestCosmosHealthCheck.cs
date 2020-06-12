// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Health;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.CosmosDb.UnitTests.Features.Health
{
    internal class TestCosmosHealthCheck : CosmosHealthCheck
    {
        public const string TestCosmosHealthCheckName = "TestCosmosHealthCheck";

        public TestCosmosHealthCheck(
            IScoped<Container> documentClient,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            IDocumentClientTestProvider testProvider,
            ILogger<CosmosHealthCheck> logger)
            : base(
                  documentClient,
                  configuration,
                  namedCosmosCollectionConfigurationAccessor,
                  TestCosmosHealthCheckName,
                  testProvider,
                  logger)
        {
        }
    }
}
