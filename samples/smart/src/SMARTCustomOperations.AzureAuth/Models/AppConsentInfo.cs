// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CA1002 // Do not expose generic lists
#pragma warning disable CA1056 // URI-like properties should not be strings

namespace SMARTCustomOperations.AzureAuth.Models
{
    public class AppConsentInfo
    {
        public string? ApplicationName { get; set; }

        public string? ApplicationDescription { get; set; }

        public string? ApplicationUrl { get; set; }

        public List<AppConsentScope> Scopes { get; } = new List<AppConsentScope>();
    }
}
