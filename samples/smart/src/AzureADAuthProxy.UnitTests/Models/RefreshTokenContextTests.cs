// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using AzureADAuthProxy.Extensions;
using AzureADAuthProxy.Models;

namespace AzureADAuthProxy.UnitTests.Models
{
    public class RefreshTokenContextTests
    {
        private static string _audience = "12345678-90ab-cdef-1234-567890abcdef";

        public static TheoryData<string> NormalTokenCollectionData =>
            new TheoryData<string>
            {
                // Confidential clients
                "grant_type=refresh_token&client_id=xxxx-xxxxx-xxxxx-xxxxx&client_secret=super-secret&refresh_token=12345678&scope=patient/*.read",

                // Public clients
                "grant_type=refresh_token&client_id=xxxx-xxxxx-xxxxx-xxxxx&refresh_token=12345678&scope=patient/*.read",
            };

        [Theory]
        [MemberData(nameof(NormalTokenCollectionData))]
        public async Task GivenNormalRefreshTokenCollection_WhenInitialized_ThenCorrectTokenContextCreated(string tokenBody)
        {
            NameValueCollection tokenBodyCol = HttpUtility.ParseQueryString(tokenBody);

            TokenContext context = TokenContext.FromFormUrlEncodedContent(tokenBodyCol, null, _audience);

            if (context.GetType() != typeof(RefreshTokenContext))
            {
                Assert.Fail("token body not parsed to the correct type");
            }

            var contextParsed = (RefreshTokenContext)context;

            // SMART required fields should always exist and match
            Assert.Equal(tokenBodyCol["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(tokenBodyCol["client_id"], contextParsed.ClientId);
            Assert.Equal(tokenBodyCol["client_secret"], contextParsed.ClientSecret);
            Assert.Equal(tokenBodyCol["refresh_token"], contextParsed.RefreshToken);
            Assert.Equal(tokenBodyCol["scope"]!.ParseScope(_audience), contextParsed.Scope);

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
            Assert.Equal(serializedData["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(serializedData["client_id"], contextParsed.ClientId);
            Assert.Equal(serializedData["client_secret"], contextParsed.ClientSecret);
            Assert.Equal(serializedData["refresh_token"], contextParsed.RefreshToken);
            Assert.Equal(serializedData["scope"], contextParsed.Scope);

            // SECRET SHOULD NOT BE SERIALOZID
            if (contextParsed.ClientSecret is not null)
            {
                Assert.DoesNotContain(contextParsed.ClientSecret!, contextParsed.ToLogString());
            }
        }
    }
}
