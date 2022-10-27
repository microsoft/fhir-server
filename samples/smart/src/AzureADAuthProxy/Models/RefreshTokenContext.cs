// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Text.Json;
using AzureADAuthProxy.Extensions;

namespace AzureADAuthProxy.Models
{
    public class RefreshTokenContext : TokenContext
    {
        /// <summary>
        /// Creates a RefreshTokenContext from the NameValueCollection from the HTTP request body.
        /// </summary>
        /// <param name="form">HTTP Form Encoded Body from Token Request.</param>
        /// <param name="audience">Azure Active Directory audience for the FHIR Server.</param>
        public RefreshTokenContext(NameValueCollection form, string audience)
        {
            if (form["grant_type"] != GrantType.refresh_token.ToString())
            {
                throw new ArgumentException("RefreshTokenContext requires the refresh token type.");
            }

            GrantType = GrantType.refresh_token;
            ClientId = form["client_id"]!;
            RefreshToken = form["refresh_token"]!;
            ClientSecret = form["client_secret"]!;

            if (form.AllKeys.Contains("scope"))
            {
                Scope = form["scope"]!.ParseScope(audience)!;
            }
        }

        public GrantType GrantType { get; set; } = GrantType.refresh_token;

        public string RefreshToken { get; set; } = default!;

        public string? Scope { get; set; } = default!;

        public string ClientId { get; set; } = default!;

        public string? ClientSecret { get; set; } = default!;

        public override string ToLogString()
        {
            ClientSecret = "***";
            return JsonSerializer.Serialize(this);
        }

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            List<KeyValuePair<string, string>> formValues = new()
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("refresh_token", RefreshToken),
                new KeyValuePair<string, string>("grant_type", GrantType.ToString()),
            };

            if (Scope is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("scope", Scope));
            }

            if (ClientSecret is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("client_secret", ClientSecret));
            }

            return new FormUrlEncodedContent(formValues);
        }

        public override void Validate()
        {
            if (GrantType != GrantType.refresh_token ||
                string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(RefreshToken))
            {
                throw new ArgumentException("Refresh TokenContext invalid");
            }
        }
    }
}
