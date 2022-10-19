using SMARTProxy.Models;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace SMARTProxy.UnitTests.Models
{
    public class LaunchContextTests
    {

        public static TheoryData<NameValueCollection> NormalAuthozizeCollectionData =>
            new TheoryData<NameValueCollection>
            {
                // Normal PKCE Flow
                HttpUtility.ParseQueryString(String.Concat(
                    "?response_type=code&client_id=xxxx-xxxxx-xxxxx-xxxxx&redirect_uri=http://localhost&scope=patient/Patient.read fhir user openid",
                    "&state=123&aud=https://workspace-fhir.fhir.azurehealthcareapis.com&code_challenge_method=S256&code_challenge=ECgEuvKylvpiOS9pF2pfu5NKoBErrx8fAWdneyiPT2E"
                )),

                // Implicit Flow
                HttpUtility.ParseQueryString(String.Concat(
                    "?response_type=code&client_id=xxxx-xxxxx-xxxxx-xxxxx&redirect_uri=http://localhost&scope=patient/Patient.read fhir user openid",
                    "&state=123&aud=https://workspace-fhir.fhir.azurehealthcareapis.com"
                )),
            };

        [Theory]
        [MemberData(nameof(NormalAuthozizeCollectionData))]
        public void GivenNormalAuthorizeCollection_WhenInitialized_ThenCorrectLaunchContextCreated(NameValueCollection authorizeParams)
        {
            LaunchContext context = new LaunchContextBuilder().FromNameValueCollection(authorizeParams).Build();

            // SMART required fields should always exist and match
            Assert.Equal(authorizeParams.GetValues("response_type")![0], context.ResponseType);
            Assert.Equal(authorizeParams.GetValues("client_id")![0], context.ClientId);
            Assert.Equal(authorizeParams.GetValues("scope")![0], context.Scope);
            Assert.Equal(authorizeParams.GetValues("aud")![0], context.Aud);

            // SMART optional fields should be null or match
            Assert.True(context.State is null || authorizeParams.GetValues("state")![0] == context.State);
            Assert.True(context.CodeChallengeMethod is null || authorizeParams.GetValues("code_challenge_method")![0] == context.CodeChallengeMethod);
            Assert.True(context.CodeChallenge is null || authorizeParams.GetValues("code_challenge")![0] == context.CodeChallenge);
        }
    }
}
