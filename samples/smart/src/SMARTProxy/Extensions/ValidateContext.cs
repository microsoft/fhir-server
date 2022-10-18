using SMARTProxy.Models;

namespace SMARTProxy.Extensions
{
    public static class ValidateContext
    {
        public static bool ValidateLaunchContext(this LaunchContext launchContext)
        {
            if (String.IsNullOrEmpty(launchContext.ResponseType) ||
                String.IsNullOrEmpty(launchContext.ClientId) ||
                String.IsNullOrEmpty(launchContext.RedirectUri) ||
                String.IsNullOrEmpty(launchContext.Scope) ||
                String.IsNullOrEmpty(launchContext.State) ||
                String.IsNullOrEmpty(launchContext.Aud) ||
                
                // TODO - validate depending on if PCE is enabled
                String.IsNullOrEmpty(launchContext.CodeChallenge) ||
                String.IsNullOrEmpty(launchContext.CodeChallengeMethod))
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public static bool ValidateResponseType(this LaunchContext launchContext)
        {
            if (!String.IsNullOrEmpty(launchContext.ResponseType) && 
                launchContext.ResponseType.ToLowerInvariant() == "code")
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}