// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class Role : IValidatableObject
    {
        [JsonConstructor]
        public Role()
        {
        }

        public Role(string name, IList<ResourcePermission> resourcePermissions, string version)
        {
            Name = name;
            ResourcePermissions = resourcePermissions;
            Version = version;
        }

        public string Name { get; set; }

        public virtual string Version { get; set; }

        public IList<ResourcePermission> ResourcePermissions { get; set; } = new List<ResourcePermission>();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult(Core.Resources.RoleNameEmpty);
            }

            if (ResourcePermissions.Count == 0)
            {
                yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, Core.Resources.ResourcePermissionEmpty, Name));
            }
            else
            {
                foreach (ResourcePermission permission in ResourcePermissions)
                {
                    if (permission.Actions.Count == 0)
                    {
                        yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, Core.Resources.RoleResourcePermissionWithNoAction, Name));
                    }
                }
            }
        }
    }
}
