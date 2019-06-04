// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Tests.Common.Mocks;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    public class ConformanceProviderTests
    {
        [Fact]
        public async Task GivenMultipleProviders_WhenRequestingAMergedCapabilitiesDocument_ThenGetsAValidCapabilityStatement()
        {
            SystemConformanceProvider systemCapabilities = CreateSystemConformanceProvider();

            var mockedCapabilities = CapabilityStatementMock.GetMockedCapabilityStatement();
            CapabilityStatementMock.SetupMockResource(mockedCapabilities, ResourceType.Account, new[] { CapabilityStatement.TypeRestfulInteraction.Create, CapabilityStatement.TypeRestfulInteraction.Read });

            var configured = Substitute.For<IConfiguredConformanceProvider>();
            configured
                .GetCapabilityStatementAsync()
                .Returns(mockedCapabilities.ToTypedElement());

            var urlResolver = Substitute.For<IUrlResolver>();
            urlResolver.ResolveMetadataUrl(Arg.Any<bool>()).Returns(new Uri("http://localhost/metadata"));

            var config = new ConformanceConfiguration()
            {
                UseStrictConformance = true,
            };

            var provider = new ConformanceProvider(
                systemCapabilities,
                configured,
                urlResolver,
                Options.Create(config));

            var typedCapabilityStatement = await provider.GetCapabilityStatementAsync();
            var capabilityStatement = typedCapabilityStatement.ToPoco() as CapabilityStatement;

            Assert.NotNull(capabilityStatement.Software);
            Assert.Equal("Microsoft FHIR Server", capabilityStatement.Software.Name);
            Assert.Equal("Microsoft Corporation", capabilityStatement.Publisher);
            Assert.NotNull(capabilityStatement.Rest.Single().Resource.Single().Interaction.FirstOrDefault(x => x.Code == CapabilityStatement.TypeRestfulInteraction.Create));
            Assert.NotNull(capabilityStatement.Rest.Single().Resource.Single().Interaction.FirstOrDefault(x => x.Code == CapabilityStatement.TypeRestfulInteraction.Read));
        }

        [Fact]
        public async Task GivenASystemConformanceProvider_WhenRequestingACapabilitiesDocument_ThenGetsAValidCapabilityStatement()
        {
            SystemConformanceProvider systemCapabilities = CreateSystemConformanceProvider();

            var capabilityStatement = await systemCapabilities.GetSystemListedCapabilitiesStatementAsync();

            Assert.NotNull(capabilityStatement.Software);
            Assert.Equal("Microsoft FHIR Server", capabilityStatement.Software.Name);
            Assert.Equal("Microsoft Corporation", capabilityStatement.Publisher);
            Assert.NotNull(capabilityStatement.Rest.Single().Resource.Single().Interaction.FirstOrDefault(x => x.Code == CapabilityStatement.TypeRestfulInteraction.Create));
            Assert.Equal(ResourceIdentity.Core(FHIRAllTypes.Account).AbsoluteUri, capabilityStatement.Rest.Single().Resource.Single().Profile.Url.ToString());
        }

        [Fact]
        public async Task GivenMultipleProviders_WhenRequestingCapabilitiesThenCancelling_ThenTheSemaphoreDoesNotReleaseTwice()
        {
            var cancellationTokenSource = new CancellationTokenSource();

            var provider = new ConformanceProvider(
                CreateSystemConformanceProvider(),
                Substitute.For<IConfiguredConformanceProvider>(),
                Substitute.For<IUrlResolver>(),
                Options.Create(new ConformanceConfiguration()));

            // Request a cancellation
            cancellationTokenSource.Cancel();

            // Attempt to get a semaphore with a cancelled token:
            var nextRequest = provider.GetCapabilityStatementAsync(cancellationTokenSource.Token);

            // Do we get cancellation exception instead of SemaphoreFullException?
            await Assert.ThrowsAnyAsync<TaskCanceledException>(async () => await nextRequest);
        }

        private static SystemConformanceProvider CreateSystemConformanceProvider()
        {
            var createCapability = Substitute.For<IProvideCapability>();
            createCapability
                .When(x => x.Build(Arg.Any<ListedCapabilityStatement>()))
                .Do(callback => callback.ArgAt<ListedCapabilityStatement>(0).TryAddRestInteraction(ResourceType.Account, CapabilityStatement.TypeRestfulInteraction.Create)
                    .TryAddRestInteraction(ResourceType.Account, CapabilityStatement.TypeRestfulInteraction.Read)
                    .TryAddRestInteraction(ResourceType.Account, CapabilityStatement.TypeRestfulInteraction.Vread));

            var owned = Substitute.For<IScoped<IEnumerable<IProvideCapability>>>();
            owned.Value.Returns(new[] { createCapability });

            var systemCapabilities = new SystemConformanceProvider(() => owned);
            return systemCapabilities;
        }
    }
}
