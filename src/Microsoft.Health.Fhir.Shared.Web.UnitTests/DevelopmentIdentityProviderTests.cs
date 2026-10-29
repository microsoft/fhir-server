// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.OpenIddict.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Web.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class DevelopmentIdentityProviderTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("fhir-api")]
        [InlineData("https://localhost:44399")]
        public async Task GivenAResource_WhenValidatingATokenRequest_ThenResourcesAreIgnored(string resource)
        {
            var clientId = Guid.NewGuid().ToString();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["DevelopmentIdentityProvider:Enabled"] = "true",
                ["DevelopmentIdentityProvider:ClientApplications:0:Id"] = clientId,
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDevelopmentIdentityProvider(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientId,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                    OpenIddictConstants.Permissions.Prefixes.Scope + "fhir-api",
                },
            });
            var factory = scope.ServiceProvider.GetRequiredService<IOpenIddictServerFactory>();
            var transaction = await factory.CreateTransactionAsync();
            transaction.EndpointType = OpenIddictServerEndpointType.Token;
            transaction.BaseUri = new Uri("https://localhost:44399/");
            transaction.RequestUri = new Uri(transaction.BaseUri, "connect/token");
            transaction.Request = new OpenIddictRequest
            {
                ClientId = clientId,
                ClientSecret = clientId,
                GrantType = OpenIddictConstants.GrantTypes.ClientCredentials,
                Scope = "fhir-api",
                Resources = resource == null ? null : [resource],
            };
            var context = new OpenIddictServerEvents.ValidateTokenRequestContext(transaction);
            var dispatcher = scope.ServiceProvider.GetRequiredService<IOpenIddictServerDispatcher>();

            await dispatcher.DispatchAsync(context);

            Assert.False(context.IsRejected, $"{context.Error}: {context.ErrorDescription}");
            Assert.Empty(transaction.Request.GetResources());
        }
    }
}
