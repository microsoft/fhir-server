// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class GetSmartConfigurationHandlerTests
    {
        public GetSmartConfigurationHandlerTests()
        {
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenSecurityConfigurationNotEnabled_Then400ExceptionThrown()
        {
            var baseUri = new System.Uri("https://fhir.example.com/");
            var request = new GetSmartConfigurationRequest(baseUri);

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = false;

            var handler = new GetSmartConfigurationHandler(Options.Create(securityConfiguration));

            OperationFailedException e = await Assert.ThrowsAsync<OperationFailedException>(() => handler.Handle(request, CancellationToken.None));
            Assert.Equal(HttpStatusCode.BadRequest, e.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenSecurityConfigurationEnabled_ThenSmartConfigurationReturned()
        {
            var baseUri = new System.Uri("https://fhir.example.com/");
            var request = new GetSmartConfigurationRequest(baseUri);
            string baseEndpoint = "http://base.endpoint";

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = baseEndpoint;

            var handler = new GetSmartConfigurationHandler(Options.Create(securityConfiguration));

            GetSmartConfigurationResponse response = await handler.Handle(request, CancellationToken.None);

            Assert.Equal(response.AuthorizationEndpoint.ToString(), baseEndpoint + "/authorize");
            Assert.Equal(response.TokenEndpoint.ToString(), baseEndpoint + "/token");
            Assert.Equal(response.Capabilities, new List<string>
                    {
                        "sso-openid-connect",
                        "permission-offline",
                        "permission-patient",
                        "permission-user",
                    });

            // Verify SMART v2 scopes are included
            Assert.NotNull(response.ScopesSupported);
            Assert.NotNull(response.CodeChallengeMethodsSupported);
            Assert.NotNull(response.GrantTypesSupported);
            Assert.NotNull(response.TokenEndpointAuthMethodsSupported);
            Assert.NotNull(response.ResponseTypesSupported);
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenBaseEndpointIsInvalid_Then400ExceptionThrown()
        {
            var baseUri = new System.Uri("https://fhir.example.com/");
            var request = new GetSmartConfigurationRequest(baseUri);
            string baseEndpoint = "invalidBaseEndpoint";

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = baseEndpoint;

            var handler = new GetSmartConfigurationHandler(Options.Create(securityConfiguration));

            OperationFailedException exception = await Assert.ThrowsAsync<OperationFailedException>(() => handler.Handle(request, CancellationToken.None));
            Assert.Equal(HttpStatusCode.BadRequest, exception.ResponseStatusCode);
        }

        [Theory]
        [InlineData("https://ehr.example.com/user/introspect", null, null)]
        [InlineData(null, "https://ehr.example.com/user/manage", null)]
        [InlineData(null, null, "https://ehr.example.com/user/revoke")]
        [InlineData("https://ehr.example.com/user/introspect", "https://ehr.example.com/user/manage", "https://ehr.example.com/user/revoke")]
        public async Task GivenASmartConfigurationHandler_WhenOtherEndpointsAreSpecifired_ThenSmartConfigurationShouldContainsOtherEndpoints(
            string introspectionEndpoint,
            string managementEndpoint,
            string revocationEndpoint)
        {
            var baseUri = new System.Uri("https://fhir.example.com/");
            var request = new GetSmartConfigurationRequest(baseUri);
            string baseEndpoint = "http://base.endpoint";

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = baseEndpoint;
            securityConfiguration.IntrospectionEndpoint = introspectionEndpoint;
            securityConfiguration.ManagementEndpoint = managementEndpoint;
            securityConfiguration.RevocationEndpoint = revocationEndpoint;

            var handler = new GetSmartConfigurationHandler(Options.Create(securityConfiguration));

            GetSmartConfigurationResponse response = await handler.Handle(request, CancellationToken.None);

            Assert.Equal(response.AuthorizationEndpoint.ToString(), baseEndpoint + "/authorize");
            Assert.Equal(response.TokenEndpoint.ToString(), baseEndpoint + "/token");

            // Verify SMART v2 endpoints
            Assert.Equal(introspectionEndpoint, response.IntrospectionEndpoint);
            Assert.Equal(managementEndpoint, response.ManagementEndpoint);
            Assert.Equal(revocationEndpoint, response.RevocationEndpoint);
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenAadSmartOnFhirProxyEnabled_ThenProxyEndpointsReturned()
        {
            var baseUri = new System.Uri("https://fhir.example.com/");
            var request = new GetSmartConfigurationRequest(baseUri);
            string baseEndpoint = "http://auth.endpoint";

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = baseEndpoint;
            securityConfiguration.EnableAadSmartOnFhirProxy = true;

            var handler = new GetSmartConfigurationHandler(Options.Create(securityConfiguration));

            GetSmartConfigurationResponse response = await handler.Handle(request, CancellationToken.None);

            Assert.Equal("https://fhir.example.com/AadSmartOnFhirProxy/authorize", response.AuthorizationEndpoint.ToString());
            Assert.Equal("https://fhir.example.com/AadSmartOnFhirProxy/token", response.TokenEndpoint.ToString());
            Assert.Equal(response.Capabilities, new List<string>
                    {
                        "sso-openid-connect",
                        "permission-offline",
                        "permission-patient",
                        "permission-user",
                    });

            // Verify SMART v2 scopes are included
            Assert.NotNull(response.ScopesSupported);
            Assert.NotNull(response.CodeChallengeMethodsSupported);
            Assert.NotNull(response.GrantTypesSupported);
            Assert.NotNull(response.TokenEndpointAuthMethodsSupported);
            Assert.NotNull(response.ResponseTypesSupported);
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenAadSmartOnFhirProxyDisabled_ThenAuthorityEndpointsReturned()
        {
            var baseUri = new System.Uri("https://fhir.example.com/");
            var request = new GetSmartConfigurationRequest(baseUri);

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = "https://logon.onmicrosoft.com/guid";
            securityConfiguration.EnableAadSmartOnFhirProxy = false;

            var handler = new GetSmartConfigurationHandler(Options.Create(securityConfiguration));

            GetSmartConfigurationResponse response = await handler.Handle(request, CancellationToken.None);

            Assert.Equal("https://logon.onmicrosoft.com/guid" + "/authorize", response.AuthorizationEndpoint.ToString());
            Assert.Equal("https://logon.onmicrosoft.com/guid" + "/token", response.TokenEndpoint.ToString());
            Assert.Equal(response.Capabilities, new List<string>
                    {
                        "sso-openid-connect",
                        "permission-offline",
                        "permission-patient",
                        "permission-user",
                    });

            // Verify SMART v2 scopes are included
            Assert.NotNull(response.ScopesSupported);
            Assert.NotNull(response.CodeChallengeMethodsSupported);
            Assert.NotNull(response.GrantTypesSupported);
            Assert.NotNull(response.TokenEndpointAuthMethodsSupported);
            Assert.NotNull(response.ResponseTypesSupported);
        }
    }
}
