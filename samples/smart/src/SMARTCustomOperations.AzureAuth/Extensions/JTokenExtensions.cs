// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json.Linq;

namespace SMARTCustomOperations.AzureAuth.Extensions
{
    public static class JTokenExtensions
    {
        public static bool IsNullOrEmpty(this JToken token)
        {
            return (token == null) ||
            (token.Type == JTokenType.Array && !token.HasValues) ||
            (token.Type == JTokenType.Object && !token.HasValues) ||
            (token.Type == JTokenType.String && string.IsNullOrEmpty(token.ToString())) ||
            (token.Type == JTokenType.Null);
        }
    }
}
