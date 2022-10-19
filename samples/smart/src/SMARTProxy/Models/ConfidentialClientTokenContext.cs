// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace SMARTProxy.Models
{
    public class ConfidentialClientTokenContext : TokenContext
    {
        public GrantType GrantType { get; set; } = default!;

        public string Code { get; set; } = default!;

        public Uri RedirectUri { get; set; } = default!;

        public string ClientId { get; set; } = default!;

        public string ClientSecret { get; set; } = default!;

        public string? CodeVerifier { get; set; } = default!;

        public override string ToLogString()
        {
            ClientSecret = "***";
            return JsonSerializer.Serialize(this);
        }

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
