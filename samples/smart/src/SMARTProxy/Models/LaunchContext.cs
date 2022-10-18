using System.Collections.Specialized;
using System.Web;

namespace SMARTProxy.Models
{
    public class LaunchContext
    {
        public string ResponseType { get; set; } = default!;
        public string ClientId { get; set; } = default!;
        public string RedirectUri { get; set; } = default!;
        public string Scope { get; set; } = default!;
        public string State { get; set; } = default!;
        public string Aud { get; set; } = default!;
        public string? CodeChallenge { get; set; } = default!;
        public string? CodeChallengeMethod { get; set; } = default!;
    }

    interface ILaunchContextBuilder
    {
        LaunchContextBuilder SetResponseType(string responseType);
        LaunchContextBuilder SetClientId(string clientId);
        LaunchContextBuilder SetRedirectUri(string redirectUri);
        LaunchContextBuilder SetScope(string scope);
        LaunchContextBuilder SetState(string state);
        LaunchContextBuilder SetAud(string aud);
        LaunchContextBuilder SetCodeChallenge(string codeChallenge);
        LaunchContextBuilder SetCodeChallengeMethod(string codeChallengeMethod);
        LaunchContextBuilder FromNameValueCollection(NameValueCollection queryParams);
    }

    public class LaunchContextBuilder : ILaunchContextBuilder
    {
        private readonly LaunchContext _launchContext = new LaunchContext();

        public LaunchContextBuilder SetResponseType(string responseType)
        {
            _launchContext.ResponseType = responseType;
            return this;
        }

        public LaunchContextBuilder SetClientId(string clientId)
        {
            _launchContext.ClientId = clientId;
            return this;
        }

        public LaunchContextBuilder SetRedirectUri(string redirectUri)
        {
            _launchContext.RedirectUri = redirectUri;
            return this;
        }

        public LaunchContextBuilder SetScope(string scope)
        {
            _launchContext.Scope = scope;
            return this;
        }

        public LaunchContextBuilder SetState(string state)
        {
            _launchContext.State = state;
            return this;
        }

        public LaunchContextBuilder SetAud(string aud)
        {
            _launchContext.Aud = aud;
            return this;
        }

        public LaunchContextBuilder SetCodeChallenge(string codeChallenge)
        {
            _launchContext.CodeChallenge = codeChallenge;
            return this;
        }

        public LaunchContextBuilder SetCodeChallengeMethod(string codeChallengeMethod)
        {
            _launchContext.CodeChallengeMethod = codeChallengeMethod;
            return this;
        }

        // #TODO - check for non-existing values
        public LaunchContextBuilder FromNameValueCollection(NameValueCollection queryParams)
        {
            SetResponseType(queryParams["response_type"]!);
            SetClientId(queryParams["client_id"]!);
            SetRedirectUri(queryParams["redirect_uri"]!);
            SetScope(queryParams["scope"]!);
            SetState(queryParams["state"]!);
            SetAud(queryParams["aud"]!);
            SetCodeChallenge(queryParams["code_challenge"]!);
            SetCodeChallengeMethod(queryParams["code_challenge_method"]!);
            return this;
        }

        public LaunchContext Build()
        {
            return _launchContext;
        }
    }
}
