// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class IdentityProvider
    {
        public IdentityProvider()
        {
        }

        public IdentityProvider(string name, string authority, List<string> audience)
        {
            Name = name;
            Authority = authority;
            Audience.Concat(audience);
        }

        public string Name { get; set; }

        public string Authority { get; set; }

        public List<string> Audience { get; } = new List<string>();

        public string Version { get; set; }
    }
}
