// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Text.Json.Serialization;

namespace SMARTProxy.Models
{
    public class ConfidentialClientTokenContext : TokenContext
    {
        /// <summary>
        /// Creates a ConfidentialClientTokenContext from the NameValueCollection from the HTTP request body.
        /// </summary>
        /// <param name="form">HTTP Form Encoded Body from Token Request</param>
        public ConfidentialClientTokenContext(NameValueCollection form)
        {
            if (form["grant_type"] != "authorization_code")
            {
                throw new ArgumentException("ConfidentialClientTokenContext requires the authorization code grant type.");
            }

            GrantType = GrantType.authorization_code;
            Code = form["code"]!;
            RedirectUri = new Uri(form["redirect_uri"]!);
            ClientId = form["client_id"]!;
            ClientSecret = form["client_secret"]!;
            CodeVerifier = form["code_verifier"]!;
        }

        public GrantType GrantType { get; } = default!;

        public string Code { get; } = default!;

        public Uri RedirectUri { get; } = default!;

        public string ClientId { get; } = default!;

        [JsonIgnore]
        public string ClientSecret { get; } = default!;

        public string? CodeVerifier { get; } = default!;

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            List<KeyValuePair<string, string>> formValues = new();

            formValues.Add(new KeyValuePair<string, string>("code", Code));
            formValues.Add(new KeyValuePair<string, string>("grant_type", GrantType.ToString()));
            formValues.Add(new KeyValuePair<string, string>("redirect_uri", RedirectUri.ToString()));
            formValues.Add(new KeyValuePair<string, string>("client_id", ClientId));
            formValues.Add(new KeyValuePair<string, string>("client_secret", ClientSecret));
            if (CodeVerifier is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("code_verifier", CodeVerifier));
            }

            return new FormUrlEncodedContent(formValues);
        }

        public override void Validate()
        {
            if (GrantType != GrantType.authorization_code ||
                string.IsNullOrEmpty(Code) ||
                string.IsNullOrEmpty(RedirectUri.ToString()) ||
                string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(ClientSecret) ||

                // TODO - do we want to force PKCE?
                string.IsNullOrEmpty(CodeVerifier))
            {
                throw new ArgumentException("TokenContext invalid");
            }
        }
    }
}
