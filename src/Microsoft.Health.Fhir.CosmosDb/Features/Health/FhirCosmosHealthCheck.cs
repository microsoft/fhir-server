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

namespace Microsoft.Health.Fhir.CosmosDb.Features.Health
{
    /// <summary>
    /// Checks for the FHIR service health.
    /// </summary>
    public class FhirCosmosHealthCheck : CosmosHealthCheck
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FhirCosmosHealthCheck"/> class.
        /// </summary>
        /// <param name="documentClient">The document client factory/</param>
        /// <param name="configuration">The CosmosDB configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="testProvider">The test provider</param>
        /// <param name="logger">The logger.</param>
        public FhirCosmosHealthCheck(
            IScoped<Container> documentClient,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            IDocumentClientTestProvider testProvider,
            ILogger<FhirCosmosHealthCheck> logger)
            : base(
                  documentClient,
                  configuration,
                  namedCosmosCollectionConfigurationAccessor,
                  Constants.CollectionConfigurationName,
                  testProvider,
                  logger)
        {
        }
    }
}
