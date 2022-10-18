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
            if (!String.IsNullOrEmpty(scope))
            {
                foreach (var s in scopes)
                {
                    // if scope starts with patient/ or encounter/ or user/ or system/ or launch or equals fhirUser
                    if (s.StartsWith("patient/") || s.StartsWith("encounter/") || s.StartsWith("user/") || s.StartsWith("system/") || s.StartsWith("launch") || s == "fhirUser")
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