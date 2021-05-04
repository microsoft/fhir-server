// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.SmartLauncher.Models
{
    public class SmartLauncherConfig
    {
#pragma warning disable CA1056 // URI-like properties should not be strings
        public string FhirServerUrl { get; set; }

        public string DefaultSmartAppUrl { get; set; }

#pragma warning restore CA1056 // URI-like properties should not be strings
        public string ClientId { get; set; }
    }
}
