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
    public class TokenContextTests
    {

        public static TheoryData<string> NormalTokenCollectionData =>
            new TheoryData<string>
            {
                // PKCE
                "grant_type=authorization_code&code=12345678&redirect_uri=http://localhost&client_id=xxxx-xxxxx-xxxxx-xxxxx&code_verifier=test",
                
                // Non-PKCE
                "grant_type=authorization_code&code=12345678&redirect_uri=http://localhost&client_id=xxxx-xxxxx-xxxxx-xxxxx",
            };

        [Theory]
        [MemberData(nameof(NormalTokenCollectionData))]
        public void GivenNormalAuthorizeCollection_WhenInitialized_ThenCorrectLaunchContextCreated(string tokenBody)
        {
            TokenContext context = TokenContext.FromFormUrlEncodedContent(tokenBody);

            if (context.GetType() != typeof(PublicClientTokenContext))
            {
                Assert.Fail("token body not parsed to the correct type");
            }

            var contextParsed = (PublicClientTokenContext)context;

            NameValueCollection tokenBodyCol = HttpUtility.ParseQueryString(tokenBody);

            // SMART required fields should always exist and match
            Assert.Equal(tokenBodyCol["grant_type"], contextParsed.GrantType.ToString());
            Assert.Equal(tokenBodyCol["code"], contextParsed.Code);
            Assert.Equal(tokenBodyCol["redirect_uri"], contextParsed.RedirectUri);
            Assert.Equal(tokenBodyCol["client_id"], contextParsed.ClientId);

            // SMART optional fields should be null or match
            Assert.True(contextParsed.CodeVerifier is null || tokenBodyCol["code_verifier"] == contextParsed.CodeVerifier);
        }
    }
}
