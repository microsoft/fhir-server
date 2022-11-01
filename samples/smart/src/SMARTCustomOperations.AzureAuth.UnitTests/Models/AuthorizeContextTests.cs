// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using SMARTCustomOperations.AzureAuth.Extensions;
using SMARTCustomOperations.AzureAuth.Models;

namespace SMARTCustomOperations.AzureAuth.UnitTests.Models
{
    public class AuthorizeContextTests
    {
        private const string AUD = "api://test-aud";

        public static TheoryData<NameValueCollection> NormalAuthozizeCollectionData =>
            new()
            {
                // Normal PKCE Flow
                HttpUtility.ParseQueryString(string.Concat(
                    "?response_type=code&client_id=xxxx-xxxxx-xxxxx-xxxxx&redirect_uri=http://localhost&scope=patient/Patient.read fhir user openid",
                    "&state=123&aud=https://my-fhir-frontend&code_challenge_method=S256&code_challenge=ECgEuvKylvpiOS9pF2pfu5NKoBErrx8fAWdneyiPT2E")),

                // Implicit Flow
                HttpUtility.ParseQueryString(string.Concat(
                    "?response_type=code&client_id=xxxx-xxxxx-xxxxx-xxxxx&redirect_uri=http://localhost&scope=patient/Patient.read fhir user openid",
                    "&state=123&aud=https://my-fhir-frontend")),
            };

        [Theory]
        [MemberData(nameof(NormalAuthozizeCollectionData))]
        public void GivenNormalAuthorizeCollection_WhenInitialized_ThenCorrectLaunchContextCreated(NameValueCollection authorizeParams)
        {
            AuthorizeContext context = new AuthorizeContext(authorizeParams).Translate(AUD);

            // SMART required fields should always exist and match
            Assert.Equal(authorizeParams["response_type"], context.ResponseType);
            Assert.Equal(authorizeParams["client_id"], context.ClientId);

            // Scopes are changed in the constructor
            authorizeParams["scope"] = context.Scope.ParseScope(AUD);
            Assert.Equal(authorizeParams["scope"], context.Scope);

            // Audience is changed in the constructor
            authorizeParams["aud"] = AUD;
            Assert.Equal(AUD, context.Audience);

            // SMART optional fields should be null or match
            Assert.True(context.State is null || authorizeParams.GetValues("state")![0] == context.State);
            Assert.True(context.CodeChallengeMethod is null || authorizeParams.GetValues("code_challenge_method")![0] == context.CodeChallengeMethod);
            Assert.True(context.CodeChallenge is null || authorizeParams.GetValues("code_challenge")![0] == context.CodeChallenge);

            // Test serialization logic
            Assert.Equal(HttpUtility.ParseQueryString(authorizeParams.ToString()!), authorizeParams);
        }
    }
}
