// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
            var request = new GetSmartConfigurationRequest();

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = false;

            var handler = new GetSmartConfigurationHandler(securityConfiguration, ModelInfoProvider.Instance);

            OperationFailedException e = await Assert.ThrowsAsync<OperationFailedException>(() => handler.Handle(request, CancellationToken.None));
            Assert.Equal(HttpStatusCode.BadRequest, e.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenSecurityConfigurationEnabled_ThenSmartConfigurationReturned()
        {
            var request = new GetSmartConfigurationRequest();
            string baseEndpoint = "http://base.endpoint";

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = baseEndpoint;

            var handler = new GetSmartConfigurationHandler(securityConfiguration, ModelInfoProvider.Instance);

            GetSmartConfigurationResponse response = await handler.Handle(request, CancellationToken.None);

            Assert.Equal(response.AuthorizationEndpoint.ToString(), baseEndpoint + "/authorize");
            Assert.Equal(response.TokenEndpoint.ToString(), baseEndpoint + "/token");
            Assert.Equal(response.Capabilities, new List<string>
                    {
                        "sso-openid-connect",
                        "permission-offline",
                        "permission-patient",
                    });
        }

        [Fact]
        public async Task GivenASmartConfigurationHandler_WhenBaseEndpointIsInvalid_Then400ExceptionThrown()
        {
            var request = new GetSmartConfigurationRequest();
            string baseEndpoint = "invalidBaseEndpoint";

            var securityConfiguration = new SecurityConfiguration();
            securityConfiguration.Authorization.Enabled = true;
            securityConfiguration.Authentication.Authority = baseEndpoint;

            var handler = new GetSmartConfigurationHandler(securityConfiguration, ModelInfoProvider.Instance);

            OperationFailedException exception = await Assert.ThrowsAsync<OperationFailedException>(() => handler.Handle(request, CancellationToken.None));
            Assert.Equal(HttpStatusCode.BadRequest, exception.ResponseStatusCode);
        }
    }
}
