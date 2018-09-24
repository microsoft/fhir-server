// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class Role
    {
        public Role()
        {
        }

        public Role(string name, IReadOnlyList<ResourcePermission> resourcePermissions)
        {
            Name = name;
            ResourcePermissions = resourcePermissions;
        }

        public string Name { get; set; }

        public virtual string Version { get; set; }

        public IReadOnlyList<ResourcePermission> ResourcePermissions { get; set; }
    }
}
