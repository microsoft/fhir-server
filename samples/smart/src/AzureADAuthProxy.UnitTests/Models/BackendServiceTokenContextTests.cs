// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using AzureADAuthProxy.Models;

namespace AzureADAuthProxy.UnitTests.Models
{
    public class BackendServiceTokenContextTests
    {
        private static string _audience = "12345678-90ab-cdef-1234-567890abcdef";

        public static TheoryData<string> NormalTokenCollectionData =>
            new TheoryData<string>
            {
                // Assertion from https://www.hl7.org/fhir/smart-app-launch/example-backend-services.html#retrieve-access-token
                "grant_type=client_credentials&scope=system/*.read&client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer&" +
                "client_assertion=eyJ0eXAiOiJKV1QiLCJraWQiOiJhZmIyN2MyODRmMmQ5Mzk1OWMxOGZhMDMyMGUzMjA2MCIsImFsZyI6IkVTMzg0In0.eyJpc3MiOiJkZW1vX2FwcF93aGF0ZXZlciIsInN1YiI6ImRlbW9fYXBwX3doYXRldmVyIiwiYXVkIjoiaHR0cHM6Ly9zbWFydC5hcmdvLnJ1bi92L3I0L3NpbS9leUp0SWpvaU1TSXNJbXNpT2lJeElpd2lhU0k2SWpFaUxDSnFJam9pTVNJc0ltSWlPaUk0TjJFek16bGtNQzA0WTJGbExUUXhPR1V0T0Rsak55MDROalV4WlRaaFlXSXpZellpZlEvYXV0aC90b2tlbiIsImp0aSI6ImQ4MDJiZDcyY2ZlYTA2MzVhM2EyN2IwODE3YjgxZTQxZTBmNzQzMzE4MTg4OTM4YjAxMmMyMDM2NmJkZmU4YTEiLCJleHAiOjE2MzM1MzIxMzR9.eHUtXmppOLIMAfBM4mFpcgJ90bDNYWQpkm7--yRS2LY5HoXwr3FpqHMTrjhK60r5kgYGFg6v9IQaUFKFpn1N2Eyty62JWxvGXRlgEDbdX9wAAr8TeWnsAT_2orfpn6wz",
            };

        [Theory]
        [MemberData(nameof(NormalTokenCollectionData))]
        public async Task GivenNormalBackendServiceTokenCollection_WhenInitialized_ThenCorrectTokenContextCreated(string tokenBody)
        {
            NameValueCollection tokenBodyCol = HttpUtility.ParseQueryString(tokenBody);

            TokenContext context = TokenContext.FromFormUrlEncodedContent(tokenBodyCol, null, _audience);

            if (context.GetType() != typeof(BackendServiceTokenContext))
            {
                Assert.Fail("token body not parsed to the correct type");
            }

            var contextParsed = (BackendServiceTokenContext)context;

            // SMART required fields should always exist and match
            Assert.Equal(tokenBodyCol["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(tokenBodyCol["client_assertion_type"], contextParsed.ClientAssertionType);
            Assert.Equal(tokenBodyCol["client_assertion"], contextParsed.ClientAssertion);

            // Scopes must point to the ./default endpoint for the audience according to AAD client credentials flow
            string expectedScope = $"{_audience}/.default";
            Assert.Equal(expectedScope, contextParsed.Scope);

            // Client ID properly decoded from token
            Assert.Equal("demo_app_whatever", contextParsed.ClientId);

            try
            {
                contextParsed.Validate();
            }
            catch (Exception ex)
            {
                Assert.Fail("Validation failed. " + ex);
            }

            // This request is not going to AAD so we expect this to throw an exception.
            Assert.Throws<InvalidOperationException>(() => contextParsed.ToFormUrlEncodedContent());

            // Test serialization logic
            var serializedData = await contextParsed.ConvertToClientCredentialsFormUrlEncodedContent("my-secret").ReadAsFormDataAsync();
            Assert.Equal(expectedScope, serializedData["scope"]);
            Assert.Equal(contextParsed.GrantType.ToString(), serializedData["grant_type"]);
            Assert.Equal(contextParsed.ClientId, serializedData["client_id"]);
            Assert.Equal("my-secret", serializedData["client_secret"]);
        }
    }
}
