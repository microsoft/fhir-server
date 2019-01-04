// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;

namespace Microsoft.Health.ControlPlane.Core.Configs
{
    public class Bootstrap
    {
        public IList<Role> Roles { get; set; }

        public IList<IdentityProvider> IdentityProviders { get; set; }
    }
}
