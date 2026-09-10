// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [RequiresIsolatedDatabase]
    public sealed class StartupForSemanticSearchTests : StartupBaseForCustomProviders
    {
        public StartupForSemanticSearchTests(IConfiguration configuration)
            : base(ConfigureVectorSearch(configuration))
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.Replace(ServiceDescriptor.Singleton<IVectorSearchParameterResolver>(new SemanticSearchTestParameterResolver()));
            services.AddSingleton<BlockingDeterministicEmbeddingClient>();
            services.Replace(ServiceDescriptor.Scoped<IEmbeddingClient>(provider => provider.GetRequiredService<BlockingDeterministicEmbeddingClient>()));
        }

        private static IConfiguration ConfigureVectorSearch(IConfiguration configuration)
        {
            configuration["FhirServer:CoreFeatures:VectorSearch:Enabled"] = "true";
            configuration["FhirServer:CoreFeatures:VectorSearch:Embedding:Endpoint"] = "https://semantic-search.test";
            configuration["FhirServer:CoreFeatures:VectorSearch:Embedding:DeploymentName"] = "deterministic";
            configuration["FhirServer:CoreFeatures:VectorSearch:Embedding:ModelName"] = "text-embedding-3-small";
            configuration["FhirServer:CoreFeatures:VectorSearch:Embedding:ModelVersion"] = "1";
            configuration["FhirServer:CoreFeatures:VectorSearch:Embedding:Dimensions"] = "1536";
            return configuration;
        }
    }
}
