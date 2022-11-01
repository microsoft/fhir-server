// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTCustomOperations.AzureAuth.Bindings
{
    /// <summary>
    /// Options for Azure Active Directory binding.
    /// </summary>
    public class AzureActiveDirectoryBindingOptions
    {
        /// <summary>
        /// Base URL for Azure Active Directory.
        /// </summary>
        public string? AzureActiveDirectoryEndpoint { get; set; }
    }
}
