// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SMARTProxy.Extensions;

namespace SMARTProxy.Models
{
#pragma warning disable CA1707, SA1300 // Need enum to match the text of the grant type.
    public enum GrantType
    {
        authorization_code,
        refresh_token,
        client_credentials,
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

        public static TokenContext FromFormUrlEncodedContent(NameValueCollection formData, string audience)
        {
            TokenContext? tokenContext = null;

            // For public apps and confidential apps
            if (formData.AllKeys.Contains("grant_type") && formData["grant_type"] == GrantType.authorization_code.ToString())
            {
                if (formData.AllKeys.Contains("client_secret"))
                {
                    tokenContext = new ConfidentialClientTokenContext(formData);
                }
                else
                {
                    tokenContext = new PublicClientTokenContext(formData);
                }
            }
            else if (formData.AllKeys.Contains("grant_type") && formData["grant_type"] == GrantType.refresh_token.ToString())
            {
                tokenContext = new RefreshTokenContext(formData, audience);
            }
            else if (formData.AllKeys.Contains("grant_type") && formData["grant_type"] == GrantType.client_credentials.ToString())
            {
                // TODO - fix this string in code?
                if (formData.AllKeys.Contains("client_assertion_type") && formData.AllKeys.Contains("client_assertion_type") &&
                    Uri.UnescapeDataString(formData["client_assertion_type"]!) == "urn:ietf:params:oauth:client-assertion-type:jwt-bearer")
                {
                    tokenContext = new ClientConfidentialAsyncTokenContext(formData, audience);
                }
            }

            if (tokenContext is null)
            {
                throw new ArgumentException("Invalid token content");
            }

            return tokenContext;
        }

        public static TokenContext FromRequest(NameValueCollection formData, HttpRequestHeaders headers, string audience, ILogger logger)
        {
            // Inferno 1 Standalone Patient App depends on symetric confidential client
            // We have no choice but to provide client secret on the tests and this forces the basic auth header in the test.
            // https://github.com/inferno-framework/smart-app-launch-test-kit/blob/b7fbba193f43b65fd00568e18591a8518210f2d0/lib/smart_app_launch/token_exchange_test.rb#L51

            var reqAuth = headers!.Authorization;

            // TODO - this may need refactoring and needs better tests / error handling
            if (reqAuth?.Scheme == "Basic" && reqAuth?.Parameter is not null)
            {
                logger?.LogTrace("Request is using basic auth via header.");
                var authParameterDecoded = reqAuth!.Parameter!.DecodeBase64().Split(":");

                formData.Add("client_id", authParameterDecoded[0]);
                formData.Add("client_secret", authParameterDecoded[1]);
            }

            return FromFormUrlEncodedContent(formData, audience);
        }
    }
}
