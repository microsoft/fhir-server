// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using CompartmentType = Microsoft.Health.Fhir.ValueSets.CompartmentType;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerSharedServiceResolutionTests
    {
        [Fact]
        public async Task GivenProductionRegistrations_WhenAChildIsDisposed_ThenSharedAliasesRemainRootOwned()
        {
            using IHost root = new HostBuilder()
                .ConfigureAppConfiguration(
                    configuration => configuration.AddInMemoryCollection(
                        new Dictionary<string, string>
                        {
                            ["FhirServer:MultiTenantApplication:Enabled"] = bool.TrueString,
                            ["FhirServer:Security:Enabled"] = bool.FalseString,
                        }))
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(
                        (context, services) =>
                        {
                            services.AddSingleton(Substitute.For<IDataStoreSearchParameterValidator>());
                            services.AddFhirServer(context.Configuration);
                        });
                    webBuilder.Configure(static _ => { });
                })
                .Build();

            IServiceProvider rootServices = root.Services;
            var policy = Assert.IsType<TenantHostedServicePolicy>(
                rootServices.GetRequiredService<ITenantHostedServicePolicy>());
            Assert.Equal(
                TenantHostedServiceDisposition.Shared,
                policy.Classify(typeof(CompartmentDefinitionManager).FullName));
            Assert.Equal(
                TenantHostedServiceDisposition.PerTenantInitializer,
                policy.Classify(typeof(RoleLoader).FullName));

            CompartmentDefinitionManager rootCompartment =
                rootServices.GetRequiredService<CompartmentDefinitionManager>();
            IHttpClientFactory rootHttpClientFactory = rootServices.GetRequiredService<IHttpClientFactory>();
            IHttpMessageHandlerFactory rootHandlerFactory =
                rootServices.GetRequiredService<IHttpMessageHandlerFactory>();
            EmbeddedSearchParameterDefinitionSource rootSearchSource =
                rootServices.GetRequiredService<EmbeddedSearchParameterDefinitionSource>();

            Assert.Same(rootCompartment, rootServices.GetRequiredService<ICompartmentDefinitionManager>());
            Assert.Same(rootHttpClientFactory, rootHandlerFactory);
            Assert.Same(rootSearchSource, rootServices.GetRequiredService<ISearchParameterDefinitionSource>());

            ITenantContainer child = await rootServices
                .GetRequiredService<ITenantContainerFactory>()
                .CreateAsync(
                    new TenantDescriptor(new TenantId("alpha"), new Uri("https://alpha.example")),
                    CancellationToken.None);

            Assert.True(child.TryAcquire(out ITenantLease lease));
            using (lease)
            {
                IServiceProvider childServices = lease.Services;

                Assert.Same(rootCompartment, childServices.GetRequiredService<CompartmentDefinitionManager>());
                Assert.Same(rootCompartment, childServices.GetRequiredService<ICompartmentDefinitionManager>());
                Assert.Same(rootHttpClientFactory, childServices.GetRequiredService<IHttpClientFactory>());
                Assert.Same(rootHandlerFactory, childServices.GetRequiredService<IHttpMessageHandlerFactory>());
                Assert.Same(rootSearchSource, childServices.GetRequiredService<EmbeddedSearchParameterDefinitionSource>());
                Assert.Same(rootSearchSource, childServices.GetRequiredService<ISearchParameterDefinitionSource>());
                Assert.DoesNotContain(
                    childServices.GetServices<IHostedService>(),
                    hostedService => hostedService is CompartmentDefinitionManager);
            }

            await child.DisposeAsync();

            Assert.Same(rootCompartment, rootServices.GetRequiredService<CompartmentDefinitionManager>());
            Assert.Same(rootHttpClientFactory, rootServices.GetRequiredService<IHttpClientFactory>());
            Assert.Same(rootHandlerFactory, rootServices.GetRequiredService<IHttpMessageHandlerFactory>());
            Assert.Same(rootSearchSource, rootServices.GetRequiredService<EmbeddedSearchParameterDefinitionSource>());

            using HttpClient client = rootHttpClientFactory.CreateClient(nameof(FhirServerSharedServiceResolutionTests));
            Assert.NotNull(rootHandlerFactory.CreateHandler(nameof(FhirServerSharedServiceResolutionTests)));
            Assert.NotEmpty(rootSearchSource.GetSystemSearchParameterResources());

            await rootCompartment.StartAsync(CancellationToken.None);
            Assert.True(rootCompartment.TryGetResourceTypes(CompartmentType.Patient, out HashSet<string> resourceTypes));
            Assert.NotEmpty(resourceTypes);
        }
    }
}
