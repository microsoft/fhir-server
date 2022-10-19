// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using Newtonsoft.Json.Linq;

namespace SMARTProxy.Extensions
{
    public static class ValidateToken
    {
        public static bool IsNullOrEmpty(this JToken token)
        {
            return (token == null) ||
            (token.Type == JTokenType.Array && !token.HasValues) ||
            (token.Type == JTokenType.Object && !token.HasValues) ||
            (token.Type == JTokenType.String && string.IsNullOrEmpty(token.ToString())) ||
            (token.Type == JTokenType.Null);
        }

        public static string EncodeBase64(this string value)
        {
            var valueBytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(valueBytes);
        }

        public static string DecodeBase64(this string value)
        {
            // Fix invalid base64 from inferno
            if (!value.EndsWith("=", StringComparison.CurrentCulture))
            {
                value = value + "=";
            }

            var valueBytes = System.Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(valueBytes);
        }
    }
}
