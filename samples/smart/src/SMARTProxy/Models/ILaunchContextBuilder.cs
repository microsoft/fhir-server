// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;

namespace SMARTProxy.Models
{
    public interface ILaunchContextBuilder
    {
        ILaunchContextBuilder SetResponseType(string responseType);

        ILaunchContextBuilder SetClientId(string clientId);

        ILaunchContextBuilder SetRedirectUri(string redirectUri);

        ILaunchContextBuilder SetScope(string scope);

        ILaunchContextBuilder SetState(string state);

        ILaunchContextBuilder SetAud(string aud);

        ILaunchContextBuilder SetCodeChallenge(string codeChallenge);

        ILaunchContextBuilder SetCodeChallengeMethod(string codeChallengeMethod);

        ILaunchContextBuilder FromNameValueCollection(NameValueCollection queryParams);

        LaunchContext Build();
    }

    public class LaunchContextBuilder : ILaunchContextBuilder
    {
        private readonly LaunchContext _launchContext = new LaunchContext();

        public ILaunchContextBuilder SetResponseType(string responseType)
        {
            _launchContext.ResponseType = responseType;
            return this;
        }

        public ILaunchContextBuilder SetClientId(string clientId)
        {
            _launchContext.ClientId = clientId;
            return this;
        }

        public ILaunchContextBuilder SetRedirectUri(string redirectUri)
        {
            _launchContext.RedirectUri = new Uri(redirectUri);
            return this;
        }

        public ILaunchContextBuilder SetScope(string scope)
        {
            _launchContext.Scope = scope;
            return this;
        }

        public ILaunchContextBuilder SetState(string state)
        {
            _launchContext.State = state;
            return this;
        }

        public ILaunchContextBuilder SetAud(string aud)
        {
            _launchContext.Aud = aud;
            return this;
        }

        public ILaunchContextBuilder SetCodeChallenge(string codeChallenge)
        {
            _launchContext.CodeChallenge = codeChallenge;
            return this;
        }

        public ILaunchContextBuilder SetCodeChallengeMethod(string codeChallengeMethod)
        {
            _launchContext.CodeChallengeMethod = codeChallengeMethod;
            return this;
        }

        // #TODO - check for non-existing values
        public ILaunchContextBuilder FromNameValueCollection(NameValueCollection queryParams)
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
