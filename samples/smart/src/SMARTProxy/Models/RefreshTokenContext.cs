// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;

namespace SMARTProxy.Models
{
    public class RefreshTokenContext : TokenContext
    {
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
