// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Web;
using SMARTProxy.Extensions;

namespace SMARTProxy.Models
{
    public class AuthorizeContext
    {
        private string _scope;

        private string _audience;

        public AuthorizeContext(NameValueCollection queryParams)
        {
            ResponseType = queryParams["response_type"]!;
            ClientId = queryParams["client_id"]!;
            _scope = queryParams["scope"]!;
            State = queryParams["state"]!;
            CodeChallenge = queryParams["code_challenge"]!;
            CodeChallengeMethod = queryParams["code_challenge_method"]!;
            _audience = queryParams["aud"]!;

            if (queryParams.AllKeys.Contains("redirect_uri"))
            {
                RedirectUri = new Uri(queryParams["redirect_uri"]!);
            }
        }

        public string ResponseType { get; } = default!;

        public string ClientId { get; } = default!;

        public Uri RedirectUri { get; } = default!;

        public string Scope => _scope;

        public string State { get; } = default!;

        public string Audience => _audience;

        public string? CodeChallenge { get; } = default!;

        public string? CodeChallengeMethod { get; } = default!;

        public AuthorizeContext Translate(string fhirServerAud)
        {
            _audience = fhirServerAud;
            _scope = Scope.ParseScope(fhirServerAud);
            return this;
        }

        public string ToRedirectQueryString()
        {
            List<string> queryStringParams = new();
            queryStringParams.Add($"response_type={ResponseType}");
            queryStringParams.Add($"redirect_uri ={HttpUtility.UrlEncode(RedirectUri.ToString())}");
            queryStringParams.Add($"client_id={HttpUtility.UrlEncode(ClientId)}");
            queryStringParams.Add($"scope={HttpUtility.UrlEncode(Scope)}");
            queryStringParams.Add($"state={HttpUtility.UrlEncode(State)}");
            queryStringParams.Add($"aud={HttpUtility.UrlEncode(Audience)}");
            queryStringParams.Add($"code_challenge={HttpUtility.UrlEncode(CodeChallenge)}");
            queryStringParams.Add($" code_challenge_method={HttpUtility.UrlEncode(CodeChallengeMethod)}");

            return string.Join("&", queryStringParams);
        }

        public bool Validate()
        {
            // TODO - Add config to force PKCE?
            if (string.IsNullOrEmpty(ResponseType) ||
                string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(RedirectUri.ToString()) ||
                string.IsNullOrEmpty(Scope) ||
                string.IsNullOrEmpty(State) ||
                string.IsNullOrEmpty(Audience))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public bool IsValidResponseType()
        {
            if (!string.IsNullOrEmpty(ResponseType) &&
                ResponseType.ToLowerInvariant() == "code")
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
