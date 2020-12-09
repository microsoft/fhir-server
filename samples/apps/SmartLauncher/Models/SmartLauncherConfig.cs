// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.SmartLauncher.Models
{
    public class SmartLauncherConfig
    {
        public string FhirServerUrl { get; set; }

        public string DefaultSmartAppUrl { get; set; }

        public string ClientId { get; set; }
    }
}
