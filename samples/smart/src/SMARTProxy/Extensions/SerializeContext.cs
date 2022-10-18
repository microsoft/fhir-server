using SMARTProxy.Models;
using System.Web;

namespace SMARTProxy.Extensions
{
    public static class SerializeContext
    {
        public static string ToRedirectQueryString(this LaunchContext launchContext, string scopes)
        {
            List<string> queryStringParams = new();
            queryStringParams.Add($"response_type={launchContext.ResponseType}");
            queryStringParams.Add($"redirect_uri ={HttpUtility.UrlEncode(launchContext.RedirectUri)}");
            queryStringParams.Add($"client_id={HttpUtility.UrlEncode(launchContext.ClientId)}");
            queryStringParams.Add($"scope={HttpUtility.UrlEncode(scopes)}");
            queryStringParams.Add($"state={HttpUtility.UrlEncode(launchContext.State)}");
            queryStringParams.Add($"aud={HttpUtility.UrlEncode(launchContext.Aud)}");
            queryStringParams.Add($"code_challenge={HttpUtility.UrlEncode(launchContext.CodeChallenge)}");
            queryStringParams.Add($" code_challenge_method={HttpUtility.UrlEncode(launchContext.CodeChallengeMethod)}");

            return string.Join("&", queryStringParams);
        }
    }
}
