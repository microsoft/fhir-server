// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Azure.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Health;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.ControlPlane.CosmosDb.Health
{
    public class ControlPlaneCosmosHealthCheck : CosmosHealthCheck
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ControlPlaneCosmosHealthCheck"/> class.
        /// </summary>
        /// <param name="documentClient">The document client factory/</param>
        /// <param name="configuration">The CosmosDB configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="testProvider">The test provider</param>
        /// <param name="logger">The logger.</param>
        public ControlPlaneCosmosHealthCheck(
            IScoped<IDocumentClient> documentClient,
            CosmosDataStoreConfiguration configuration,
            IOptionsSnapshot<CosmosCollectionConfiguration> namedCosmosCollectionConfigurationAccessor,
            IDocumentClientTestProvider testProvider,
            ILogger<ControlPlaneCosmosHealthCheck> logger)
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
