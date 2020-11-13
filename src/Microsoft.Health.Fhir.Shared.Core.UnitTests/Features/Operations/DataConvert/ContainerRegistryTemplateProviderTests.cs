// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.DataConvert
{
    public class ContainerRegistryTemplateProviderTests
    {
        private ContainerRegistryTemplateProvider _containerRegistryTemplateProvider;
        private MemoryCache _cache;

        public ContainerRegistryTemplateProviderTests()
        {
            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(default, default).ReturnsForAnyArgs("Bearer faketoken");

            var dataConvertConfig = new DataConvertConfiguration
            {
                Enabled = true,
                ProcessTimeoutThreshold = TimeSpan.FromMilliseconds(50),
            };

            var registry = new ContainerRegistryInfo
            {
                ContainerRegistryServer = "test.azurecr.io",
            };
            dataConvertConfig.ContainerRegistries.Add(registry);

            var config = Options.Create(new TemplateLayerConfiguration());
            _cache = new MemoryCache(new MemoryCacheOptions());

            ITemplateSetProviderFactory factory = new TemplateSetProviderFactory(_cache, config);
            _containerRegistryTemplateProvider = new ContainerRegistryTemplateProvider(tokenProvider, factory, new NullLogger<ContainerRegistryTemplateProvider>());
        }

        [Fact]
        public async Task GivenDefaultTemplateReference_WhenFetchingTemplates_DefaultTemplateCollectionShouldReturn()
        {
            var templateReference = ImageInfo.DefaultTemplateImageReference;
            var templateCollection = await _containerRegistryTemplateProvider.GetTemplateCollectionAsync(templateReference, CancellationToken.None);

            Assert.NotEmpty(templateCollection);
        }

        [Fact]
        public async Task GivenAnInvalidToken_WhenFetchingCustomTemplates_ExceptionShouldBeThrown()
        {
            var templateReference = "test.azurecr.io/templates:latest";
            await Assert.ThrowsAsync<FetchTemplateCollectionFailedException>(() => _containerRegistryTemplateProvider.GetTemplateCollectionAsync(templateReference, CancellationToken.None));
        }
    }
}
