// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Conformance.Providers;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Get;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class GetSmartConfigurationHandlerTests
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly IWellKnownConfigurationProvider _configurationProvider;
        private readonly GetSmartConfigurationHandler _handler;

        public GetSmartConfigurationHandlerTests()
        {
            _securityConfiguration = new SecurityConfiguration();
            _securityConfiguration.Authorization.Enabled = true;

            _configurationProvider = Substitute.For<IWellKnownConfigurationProvider>();

            _handler = new GetSmartConfigurationHandler(
                Options.Create(_securityConfiguration),
                _configurationProvider,
                NullLogger<GetSmartConfigurationHandler>.Instance);
        }

        [Fact]
        public async Task GivenAuthorizationNotEnabled_WhenHandleCalled_Then400ExceptionThrown()
        {
            _securityConfiguration.Authorization.Enabled = false;

            OperationFailedException e = await Assert.ThrowsAsync<OperationFailedException>(() => _handler.Handle(new GetSmartConfigurationRequest(), CancellationToken.None));

            Assert.Equal(HttpStatusCode.BadRequest, e.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenNoConfigurationsProvided_WhenHandleCalled_Then400ExceptionThrown()
        {
            OperationFailedException e = await Assert.ThrowsAsync<OperationFailedException>(() => _handler.Handle(new GetSmartConfigurationRequest(), CancellationToken.None));

            Assert.Equal(HttpStatusCode.BadRequest, e.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenNoSmartConfigurationAndInvalidOpenIdConfiguration_WhenHandleCalled_Then400ExceptionThrown()
        {
            _configurationProvider.GetOpenIdConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetOpenIdConfiguration(null, null));

            OperationFailedException e = await Assert.ThrowsAsync<OperationFailedException>(() => _handler.Handle(new GetSmartConfigurationRequest(), CancellationToken.None));

            Assert.Equal(HttpStatusCode.BadRequest, e.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenSmartConfigurationProvided_WhenHandleCalled_ThenSmartConfigurationReturned()
        {
            var capabilities = new List<string>() { "launch-ehr", "launch-standalone", "client-public" };

            GetSmartConfigurationResponse configuration = GetSmartConfiguration(capabilities);

            _configurationProvider.GetSmartConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);

            GetSmartConfigurationResponse response = await _handler.Handle(new GetSmartConfigurationRequest(), CancellationToken.None);

            Assert.Equal("https://testhost:44312/smart/auth", response.AuthorizationEndpoint.AbsoluteUri);
            Assert.Equal("https://testhost:44312/smart/token", response.TokenEndpoint.AbsoluteUri);
            Assert.Equal(capabilities, response.Capabilities);
        }

        [Fact]
        public async Task GivenSmartConfigurationNotProvided_WhenHandleCalled_ThenSmartConfigurationUsingOpenIdReturned()
        {
            string openIdAuthorization = "https://testhost:44312/openid/auth";
            string openIdToken = "https://testhost:44312/openid/token";

            _configurationProvider.GetOpenIdConfigurationAsync(Arg.Any<CancellationToken>()).Returns(GetOpenIdConfiguration(openIdAuthorization, openIdToken));

            GetSmartConfigurationResponse response = await _handler.Handle(new GetSmartConfigurationRequest(), CancellationToken.None);

            Assert.Equal(openIdAuthorization, response.AuthorizationEndpoint.AbsoluteUri);
            Assert.Equal(openIdToken, response.TokenEndpoint.AbsoluteUri);
            Assert.Equal(GetFallbackCapabilities(), response.Capabilities);
        }

        [Fact]
        public async Task GivenSmartConfigurationHasNoCapabilities_WhenHandleCalled_ThenSmartConfigurationReturnedWithFallbackCapabilities()
        {
            GetSmartConfigurationResponse configuration = GetSmartConfiguration();

            _configurationProvider.GetSmartConfigurationAsync(Arg.Any<CancellationToken>()).Returns(configuration);

            GetSmartConfigurationResponse response = await _handler.Handle(new GetSmartConfigurationRequest(), CancellationToken.None);

            Assert.Equal("https://testhost:44312/smart/auth", response.AuthorizationEndpoint.AbsoluteUri);
            Assert.Equal("https://testhost:44312/smart/token", response.TokenEndpoint.AbsoluteUri);
            Assert.Equal(GetFallbackCapabilities(), response.Capabilities);
        }

        private OpenIdConfigurationResponse GetOpenIdConfiguration(string authorization, string token)
        {
            return new OpenIdConfigurationResponse(authorization != null ? new Uri(authorization) : null, token != null ? new Uri(token) : null);
        }

        private GetSmartConfigurationResponse GetSmartConfiguration(List<string> capabilities = null)
        {
            return new GetSmartConfigurationResponse(
                null,
                null,
                new Uri("https://testhost:44312/smart/auth"),
                null,
                new Uri("https://testhost:44312/smart/token"),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                capabilities,
                null);
        }

        private ICollection<string> GetFallbackCapabilities()
        {
            return new List<string>
            {
                "sso-openid-connect",
                "permission-offline",
                "permission-patient",
                "permission-user",
            };
        }
    }
}
