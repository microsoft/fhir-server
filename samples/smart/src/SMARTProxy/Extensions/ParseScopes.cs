// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace SMARTProxy.Extensions
{
    public static class ParseScopes
    {
        public static string ParseScope(this string scope, string clientID)
        {
            var scopesBuilder = new StringBuilder();
            string scopeURI = $"api://{clientID}";

            var scopes = scope.Split(' ');
            if (!string.IsNullOrEmpty(scope))
            {
                foreach (var s in scopes)
                {
                    // if scope starts with patient/ or encounter/ or user/ or system/ or launch or equals fhirUser
                    if (s.StartsWith("patient/", StringComparison.InvariantCulture) ||
                        s.StartsWith("encounter/", StringComparison.InvariantCulture) ||
                        s.StartsWith("user/", StringComparison.InvariantCulture) ||
                        s.StartsWith("system/", StringComparison.InvariantCulture) ||
                        s.StartsWith("launch", StringComparison.InvariantCulture) ||
                        s == "fhirUser")
                    {
                                                // Azure AD v2.0 uses fully qualified scope URIs
                        // and does not allow '/'. Therefore, we need to
                        // replace '/' with '.' in the scope URI
                        scopesBuilder.Append($"{scopeURI}/{s.Replace('/', '.')} ");
                    }
                    else
                    {
                        scopesBuilder.Append($"{s} ");
                    }
                }
            }

            var newScopes = scopesBuilder.ToString().TrimEnd();

            return newScopes;
        }
    }
}
