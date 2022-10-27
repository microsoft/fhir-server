// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Net.Http.Headers;
using System.Web;
using AzureADAuthProxy.Models;

namespace AzureADAuthProxy.UnitTests.Models
{
    public class TokenContextTests
    {
        // Base64 encoding of "username:password"
        private const string TestBasicAuthParameterNormal = "dXNlcm5hbWU6cGFzc3dvcmQ=";
        private const string TestBasicAuthParameterInvalidLikeInfernoDoes = "dXNlcm5hbWU6cGFzc3dvcmQ";

        [Theory]
        [InlineData(TestBasicAuthParameterNormal)]
        [InlineData(TestBasicAuthParameterInvalidLikeInfernoDoes)]
        public void GivenATokenStringAndBasicAuthorizaton_WhenInitialized_BasicAuthAddedToBody(string parameter)
        {
            string tokenBody = "grant_type=authorization_code&code=12345678&redirect_uri=http://localhost&client_id=xxxx-xxxxx-xxxxx-xxxxx";
            string testAud = "api://test";
            var auth = new AuthenticationHeaderValue("Basic", parameter);

            NameValueCollection tokenBodyCol = HttpUtility.ParseQueryString(tokenBody);
            TokenContext context = TokenContext.FromFormUrlEncodedContent(tokenBodyCol, auth, testAud);

            if (context.GetType() != typeof(ConfidentialClientTokenContext))
            {
                Assert.Fail("Authorization header should make this a confidential client.");
            }

            var contextParsed = (ConfidentialClientTokenContext)context;

            // SMART required fields should always exist and match
            Assert.Equal("username", contextParsed.ClientId);
            Assert.Equal("password", contextParsed.ClientSecret);
        }
    }
}
