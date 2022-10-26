// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace SMARTProxy.Extensions
{
    public static class ScopeExtensions
    {
        public static string ParseScope(this string scopesString, string scopeAudience)
        {
            var scopesBuilder = new StringBuilder();

            if (!string.IsNullOrEmpty(scopesString))
            {
                var scopes = scopesString.Replace('+', ' ').Split(' ');

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
                        var formattedScope = s.Replace("/", ".", StringComparison.InvariantCulture);
                        formattedScope = formattedScope.Replace("*", "all", StringComparison.InvariantCulture);
                        formattedScope = $"{scopeAudience}/{formattedScope}";
                        scopesBuilder.Append(formattedScope);
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
