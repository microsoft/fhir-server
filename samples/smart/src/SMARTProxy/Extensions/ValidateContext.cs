// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using SMARTProxy.Models;

namespace SMARTProxy.Extensions
{
    public static class ValidateContext
    {
        public static bool ValidateLaunchContext(this LaunchContext launchContext)
        {
            if (string.IsNullOrEmpty(launchContext.ResponseType) ||
                string.IsNullOrEmpty(launchContext.ClientId) ||
                string.IsNullOrEmpty(launchContext.RedirectUri.ToString()) ||
                string.IsNullOrEmpty(launchContext.Scope) ||
                string.IsNullOrEmpty(launchContext.State) ||
                string.IsNullOrEmpty(launchContext.Aud) ||

                // TODO - validate depending on if PCE is enabled
                string.IsNullOrEmpty(launchContext.CodeChallenge) ||
                string.IsNullOrEmpty(launchContext.CodeChallengeMethod))
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
            if (!string.IsNullOrEmpty(launchContext.ResponseType) &&
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
