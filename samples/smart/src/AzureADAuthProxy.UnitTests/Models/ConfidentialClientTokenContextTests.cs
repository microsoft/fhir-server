// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using AzureADAuthProxy.Models;

namespace AzureADAuthProxy.UnitTests.Models
{
    public class ConfidentialClientTokenContextTests
    {
        private static string _audience = "12345678-90ab-cdef-1234-567890abcdef";

        public static TheoryData<string> NormalTokenCollectionData =>
            new TheoryData<string>
            {
                // PKCE
                "grant_type=authorization_code&code=12345678&redirect_uri=http%3A%2F%2Flocalhost%2F&client_id=xxxx-xxxxx-xxxxx-xxxxx&client_secret=super-secret&code_verifier=test",

                // Non-PKCE
                "grant_type=authorization_code&code=12345678&redirect_uri=http%3A%2F%2Flocalhost%2F&client_id=xxxx-xxxxx-xxxxx-xxxxx&client_secret=super-secret",
            };

        [Theory]
        [MemberData(nameof(NormalTokenCollectionData))]
        public async Task GivenNormalConfidentialClientTokenCollection_WhenInitialized_ThenCorrectTokenContextCreated(string tokenBody)
        {
            NameValueCollection tokenBodyCol = HttpUtility.ParseQueryString(tokenBody);

            TokenContext context = TokenContext.FromFormUrlEncodedContent(tokenBodyCol, null, _audience);

            if (context.GetType() != typeof(ConfidentialClientTokenContext))
            {
                Assert.Fail("token body not parsed to the correct type");
            }

            var contextParsed = (ConfidentialClientTokenContext)context;

            // SMART required fields should always exist and match
            Assert.Equal(tokenBodyCol["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(tokenBodyCol["code"], contextParsed.Code);
            Assert.Equal(tokenBodyCol["redirect_uri"], contextParsed.RedirectUri.ToString());
            Assert.Equal(tokenBodyCol["client_id"], contextParsed.ClientId);
            Assert.Equal(tokenBodyCol["client_secret"], contextParsed.ClientSecret);

            // SMART optional fields should be null or match
            Assert.True(contextParsed.CodeVerifier is null || tokenBodyCol["code_verifier"] == contextParsed.CodeVerifier);

            try
            {
                contextParsed.Validate();
            }
            catch (Exception ex)
            {
                Assert.Fail("Validation failed. " + ex);
            }

            // Test serialization logic
            var serializedData = await contextParsed.ToFormUrlEncodedContent().ReadAsFormDataAsync();
            Assert.Equal(serializedData["code"], contextParsed.Code);
            Assert.Equal(serializedData["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(serializedData["redirect_uri"], contextParsed.RedirectUri.ToString());
            Assert.Equal(serializedData["client_id"], contextParsed.ClientId);
            Assert.Equal(serializedData["client_secret"], contextParsed.ClientSecret);

            // SECRET SHOULD NOT BE SERIALOZID
            Assert.DoesNotContain(contextParsed.ClientSecret, contextParsed.ToLogString());
        }
    }
}
