// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Web
{
    public class DevelopmentIdentityProviderApplicationConfiguration
    {
        public string Id { get; set; }

        public IList<string> Roles { get; } = new List<string>();
    }
}
