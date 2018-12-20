// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac.Roles
{
    public class Role
    {
        [JsonConstructor]
        protected Role()
        {
        }

        public Role(string name, IList<ResourcePermission> resourcePermissions,  string version)
        {
            Name = name;
            ResourcePermissions = resourcePermissions;
            Version = version;
        }

        public string Name { get; set; }

        public virtual string Version { get; set; }

        public IList<ResourcePermission> ResourcePermissions { get; internal set; } = new List<ResourcePermission>();
    }
}
