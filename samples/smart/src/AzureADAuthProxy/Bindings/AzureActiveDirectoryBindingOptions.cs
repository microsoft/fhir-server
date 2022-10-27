// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureADAuthProxy.Bindings
{
    // <summary>
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
