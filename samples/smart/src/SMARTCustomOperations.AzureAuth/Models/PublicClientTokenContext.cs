// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;

namespace SMARTCustomOperations.AzureAuth.Models
{
    public class PublicClientTokenContext : TokenContext
    {
        /// <summary>
        /// Creates a PublicClientTokenContext from the NameValueCollection from the HTTP request body.
        /// </summary>
        /// <param name="form">HTTP Form Encoded Body from Token Request</param>
        public PublicClientTokenContext(NameValueCollection form)
        {
            if (form["grant_type"] != "authorization_code")
            {
                throw new ArgumentException("PublicClientTokenContext requires the authorization code grant type.");
            }

            GrantType = GrantType.authorization_code;
            Code = form["code"]!;
            ClientId = form["client_id"]!;
            CodeVerifier = form["code_verifier"]!;

            if (form.AllKeys.Contains("redirect_uri"))
            {
                RedirectUri = new Uri(form["redirect_uri"]!);
            }
        }

        public GrantType GrantType { get; set; } = default!;

        public string Code { get; set; } = default!;

        public Uri RedirectUri { get; set; } = default!;

        public string ClientId { get; set; } = default!;

        public string? CodeVerifier { get; set; } = default!;

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            List<KeyValuePair<string, string>> formValues = new();

            formValues.Add(new KeyValuePair<string, string>("code", Code));
            formValues.Add(new KeyValuePair<string, string>("grant_type", GrantType.ToString()));
            formValues.Add(new KeyValuePair<string, string>("redirect_uri", RedirectUri.ToString()));
            formValues.Add(new KeyValuePair<string, string>("client_id", ClientId));
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
                string.IsNullOrEmpty(ClientId))

                // TODO - do we want a switch for PKCE?
                // string.IsNullOrEmpty(CodeVerifier)
            {
                throw new ArgumentException("TokenContext invalid");
            }
        }
    }
}
