// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Web
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Referenced by other assembles via shared projects.")]
    public sealed class DevelopmentIdentityProviderApplicationConfiguration
    {
        public string Id { get; set; }

        public IList<string> Roles { get; } = new List<string>();
    }
}
