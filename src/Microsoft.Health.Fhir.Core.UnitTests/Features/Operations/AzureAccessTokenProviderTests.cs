// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureAccessTokenProviderTests
    {
        [Fact]
        public async Task GivenManagedIdentityAuthenticationFailure_WhenGetAccessToken_ThenAccessTokenProviderExceptionShouldBeThrown()
        {
            var tokenProvider = new AzureAccessTokenProvider(
                new AuthenticationFailedTokenCredential(),
                new NullLogger<AzureAccessTokenProvider>());

            await Assert.ThrowsAsync<AccessTokenProviderException>(
                () => tokenProvider.GetAccessTokenForResourceAsync(new Uri("https://test.azurecr.io"), CancellationToken.None));
        }

        private sealed class AuthenticationFailedTokenCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                throw new AuthenticationFailedException("Identity not found");
            }

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            {
                throw new AuthenticationFailedException("Identity not found");
            }
        }
    }
}
