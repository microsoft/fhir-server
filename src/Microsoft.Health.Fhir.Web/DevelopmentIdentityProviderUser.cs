// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Web
{
    public class DevelopmentIdentityProviderUser
    {
        public string SubjectId { get; set; }

        public string UserName { get; set; }

        public string Password { get; set; }

        public IReadOnlyList<string> Roles { get; set; }
    }
}
