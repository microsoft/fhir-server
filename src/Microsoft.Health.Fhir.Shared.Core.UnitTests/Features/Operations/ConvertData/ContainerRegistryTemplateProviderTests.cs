// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ContainerRegistry;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.Liquid.Converter.Models;
using Microsoft.Health.Fhir.TemplateManagement.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.ConvertData
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ContainerRegistryTemplateProviderTests
    {
        [Fact]
        public async Task GivenDefaultTemplateReference_WhenFetchingTemplatesWithAcrTokenProvider_DefaultTemplateCollectionShouldReturn()
        {
            var containerRegistryTemplateProvider = GetAcrTemplateProvider();
            foreach (var templateInfo in DefaultTemplateInfo.DefaultTemplateMap.Values)
            {
                var templateReference = templateInfo.ImageReference;
                var templateCollection = await containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithDefaultTemplateReference(templateReference), CancellationToken.None);

                Assert.NotEmpty(templateCollection);
            }
        }

        [Fact]
        public async Task GivenDefaultTemplateReference_WhenFetchingTemplatesWithDefaultTokenProvider_DefaultTemplateCollectionShouldReturn()
        {
            var containerRegistryTemplateProvider = GetDefaultTemplateProvider();
            foreach (var templateInfo in DefaultTemplateInfo.DefaultTemplateMap.Values)
            {
                var templateReference = templateInfo.ImageReference;
                var templateCollection = await containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithDefaultTemplateReference(templateReference), CancellationToken.None);

                Assert.NotEmpty(templateCollection);
            }
        }

        [Fact]
        public async Task GivenAnInvalidToken_WhenFetchingCustomTemplates_ExceptionShouldBeThrown()
        {
            var containerRegistryTemplateProvider = GetAcrTemplateProvider();
            var templateReference = "test.azurecr.io/templates:latest";

            await Assert.ThrowsAsync<ContainerRegistryNotAuthorizedException>(() => containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithCustomTemplateReference(templateReference), CancellationToken.None));
        }

        [Fact]
        public async Task GivenDefaultTokenProvider_WhenFetchingCustomTemplates_ExceptionShouldBeThrown()
        {
            var containerRegistryTemplateProvider = GetDefaultTemplateProvider();
            var templateReference = "test.azurecr.io/templates:latest";

            await Assert.ThrowsAsync<ContainerRegistryAuthenticationException>(() => containerRegistryTemplateProvider.GetTemplateCollectionAsync(GetRequestWithCustomTemplateReference(templateReference), CancellationToken.None));
        }

        private IConvertDataTemplateProvider GetDefaultTemplateProvider()
        {
            ILogger<DefaultTokenProvider> logger = Substitute.For<ILogger<DefaultTokenProvider>>();
            IContainerRegistryTokenProvider tokenProvider = Substitute.For<DefaultTokenProvider>(logger);

            tokenProvider.GetTokenAsync(default, default);

            var convertDataConfig = new ConvertDataConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromSeconds(1),
            };
            convertDataConfig.ContainerRegistryServers.Add("test.azurecr.io");

            var config = Options.Create(convertDataConfig);
            return new ContainerRegistryTemplateProvider(tokenProvider, config, new NullLogger<ContainerRegistryTemplateProvider>());
        }

        private IConvertDataTemplateProvider GetAcrTemplateProvider()
        {
            IAccessTokenProvider aadTokenProvider = Substitute.For<IAccessTokenProvider>();
            IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
            IOptions<ConvertDataConfiguration> convertDataConfiguration = Substitute.For<IOptions<ConvertDataConfiguration>>();
            ILogger<AzureContainerRegistryAccessTokenProvider> logger = Substitute.For<ILogger<AzureContainerRegistryAccessTokenProvider>>();

            IContainerRegistryTokenProvider tokenProvider = Substitute.For<AzureContainerRegistryAccessTokenProvider>(aadTokenProvider, httpClientFactory, convertDataConfiguration, logger);
            tokenProvider.GetTokenAsync(default, default).ReturnsForAnyArgs("Bearer faketoken");

            var convertDataConfig = new ConvertDataConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromSeconds(1),
            };
            convertDataConfig.ContainerRegistryServers.Add("test.azurecr.io");

            var config = Options.Create(convertDataConfig);
            return new ContainerRegistryTemplateProvider(tokenProvider, config, new NullLogger<ContainerRegistryTemplateProvider>());
        }

        private ConvertDataRequest GetRequestWithDefaultTemplateReference(string templateReference)
        {
            return new ConvertDataRequest(Samples.SampleHl7v2Message, DataType.Hl7v2, templateReference.Split('/')[0], true, templateReference, "ADT_A01");
        }

        private ConvertDataRequest GetRequestWithCustomTemplateReference(string templateReference)
        {
            return new ConvertDataRequest(Samples.SampleHl7v2Message, DataType.Hl7v2, templateReference.Split('/')[0], false, templateReference, "ADT_A01");
        }
    }
}
