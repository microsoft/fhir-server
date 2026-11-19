// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Api.OpenIddict.Controllers;
using Microsoft.Health.Fhir.Api.OpenIddict.Extensions;
using Microsoft.Health.Fhir.Api.OpenIddict.Services;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
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

        [Theory]
        [InlineData("fhir-api", "fhir-api")]
        [InlineData("patient/Observation.read", "fhir-api")]
        [InlineData("wrongAudienceClient", "wrongAudienceClient")]
        public async Task GivenAScope_WhenIssuingAClientCredentialsToken_ThenTheTokenIsIssuedForTheResourceTheScopeStandsFor(string scope, string expectedResource)
        {
            // Arrange
            var clientId = Guid.NewGuid().ToString();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["DevelopmentIdentityProvider:Enabled"] = "true",
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDevelopmentIdentityProvider(configuration);
            await using var provider = services.BuildServiceProvider();

            // The hosted service registers the development scopes and the resources they stand for.
            var applicationCreater = provider.GetServices<IHostedService>().OfType<OpenIddictApplicationCreater>().Single();
            await applicationCreater.StartAsync(CancellationToken.None);
            await applicationCreater.ExecuteTask;

            await using var serviceScope = provider.CreateAsyncScope();
            var applications = serviceScope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            await applications.CreateAsync(new OpenIddictApplicationDescriptor
            {
                ClientId = clientId,
                ClientSecret = clientId,
                Permissions =
                {
                    OpenIddictConstants.Permissions.Endpoints.Token,
                    OpenIddictConstants.Permissions.GrantTypes.ClientCredentials,
                },
            });
            var transaction = await serviceScope.ServiceProvider.GetRequiredService<IOpenIddictServerFactory>().CreateTransactionAsync();
            transaction.Request = new OpenIddictRequest
            {
                ClientId = clientId,
                GrantType = OpenIddictConstants.GrantTypes.ClientCredentials,
                Scope = scope,
            };
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Host = new HostString("localhost");
            httpContext.Features.Set(new OpenIddictServerAspNetCoreFeature { Transaction = transaction });
            var controller = serviceScope.ServiceProvider.GetRequiredService<OpenIddictAuthorizationController>();
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            // Act
            IActionResult result = await controller.Token();

            // Assert
            var signIn = Assert.IsType<SignInResult>(result);
            Assert.Equal([expectedResource], signIn.Principal.GetResources());
        }

        [Fact]
        public void GivenATestAuthEnvironment_WhenBuildingTheConfiguration_ThenTheClaimOpenIddictCarriesTheClientInIsAPrincipalClaim()
        {
            // Arrange
            string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.json");
            File.WriteAllText(filePath, """{ "users": [], "clientApplications": [ { "id": "client", "roles": [] } ] }""");
            try
            {
                var existingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["TestAuthEnvironment:FilePath"] = filePath,
                }).Build();

                // Act
                IConfigurationRoot configuration = new ConfigurationBuilder()
                    .AddDevelopmentAuthEnvironmentIfConfigured(existingConfiguration)
                    .Build();

                // Assert
                string[] principalClaims = configuration.GetSection("FhirServer:Security:PrincipalClaims").Get<string[]>();
                Assert.Contains(OpenIddictConstants.Claims.ClientId, principalClaims);
            }
            finally
            {
                File.Delete(filePath);
            }
        }
    }
}
