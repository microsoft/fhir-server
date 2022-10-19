// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using SMARTProxy.Models;

namespace SMARTProxy.UnitTests.Models
{
    public class ConfidentialClientTokenContextTests
    {
        private static string _audience = "12345678-90ab-cdef-1234-567890abcdef";

        public static TheoryData<string> NormalTokenCollectionData =>
            new TheoryData<string>
            {
                // PKCE
                "grant_type=authorization_code&code=12345678&redirect_uri=http%3A%2F%2Flocalhost&client_id=xxxx-xxxxx-xxxxx-xxxxx&client_secret=super-secret&code_verifier=test",

                // Non-PKCE
                "grant_type=authorization_code&code=12345678&redirect_uri=http%3A%2F%2Flocalhost&client_id=xxxx-xxxxx-xxxxx-xxxxx&client_secret=super-secret",
            };

        [Theory]
        [MemberData(nameof(NormalTokenCollectionData))]
        public void GivenNormalAuthorizeCollection_WhenInitialized_ThenCorrectLaunchContextCreated(string tokenBody)
        {
            TokenContext context = TokenContext.FromFormUrlEncodedContent(tokenBody, _audience);

            if (context.GetType() != typeof(PublicClientTokenContext))
            {
                Assert.Fail("token body not parsed to the correct type");
            }

            var contextParsed = (PublicClientTokenContext)context;

            NameValueCollection tokenBodyCol = HttpUtility.ParseQueryString(tokenBody);

            // SMART required fields should always exist and match
            Assert.Equal(tokenBodyCol["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(tokenBodyCol["code"], contextParsed.Code);
            Assert.Equal(tokenBodyCol["redirect_uri"], contextParsed.RedirectUri.ToString());
            Assert.Equal(tokenBodyCol["client_id"], contextParsed.ClientId);

            // SMART optional fields should be null or match
            Assert.True(contextParsed.CodeVerifier is null || tokenBodyCol["code_verifier"] == contextParsed.CodeVerifier);
        }
    }
}
