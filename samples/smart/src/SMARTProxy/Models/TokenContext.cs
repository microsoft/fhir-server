// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using System.Web;
using SMARTProxy.Extensions;

namespace SMARTProxy.Models
{
#pragma warning disable CA1707, SA1300 // Need enum to match the text of the grant type.
    public enum GrantType
    {
        authorization_code,
        refresh_token,
    }
#pragma warning restore CA1707, SA1300 // Need enum to match the text of the grant type.

    public abstract class TokenContext
    {
        public virtual string ToLogString()
        {
            return JsonSerializer.Serialize(this);
        }

        public abstract void Validate();

        public abstract FormUrlEncodedContent ToFormUrlEncodedContent();

        public static TokenContext FromFormUrlEncodedContent(string formData)
        {
            var formDataCollection = HttpUtility.ParseQueryString(formData);
            TokenContext? tokenContext = null;

            // For public apps and confidential apps
            if (formDataCollection.AllKeys.Contains("grant_type") && formDataCollection["grant_type"] == "authorization_code")
            {
                if (formDataCollection.AllKeys.Contains("client_secret"))
                {
                    tokenContext = new ConfidentialClientTokenContext()
                    {
                        GrantType = GrantType.authorization_code,
                        Code = formDataCollection["code"]!,
                        RedirectUri = new Uri(formDataCollection["redirect_uri"]!),
                        ClientId = formDataCollection["client_id"]!,
                        ClientSecret = formDataCollection["client_secret"]!,
                        CodeVerifier = formDataCollection["code_verifier"]!,
                    };
                }
                else
                {
                    tokenContext = new PublicClientTokenContext()
                    {
                        GrantType = GrantType.authorization_code,
                        Code = formDataCollection["code"]!,
                        RedirectUri = new Uri(formDataCollection["redirect_uri"]!),
                        ClientId = formDataCollection["client_id"]!,
                        CodeVerifier = formDataCollection["code_verifier"]!,
                    };
                }
            }
            else if (formDataCollection.AllKeys.Contains("grant_type") && formDataCollection["grant_type"] == "refresh_token")
            {
                tokenContext = new RefreshTokenContext()
                {
                    GrantType = GrantType.refresh_token,
                    ClientId = formDataCollection["client_id"]!,
                    Scope = formDataCollection.AllKeys.Contains("scope") && formDataCollection.AllKeys.Contains("client_id") ? formDataCollection["scope"]!.ParseScope(formDataCollection["client_id"]!)! : null,
                    RefreshToken = formDataCollection["refresh_token"]!,
                    ClientSecret = formDataCollection["client_secret"]!,
                };
            }

            // TODO - add backend services

            if (tokenContext is null)
            {
                throw new ArgumentException("Invalid token content");
            }

            return tokenContext;
        }
    }
}
