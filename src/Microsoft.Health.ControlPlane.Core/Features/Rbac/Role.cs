// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class Role : IValidatableObject
    {
        [JsonConstructor]
        public Role()
        {
        }

        public Role(string name, IList<ResourcePermission> resourcePermissions)
            : this(name, resourcePermissions, null)
        {
            Name = name;
            ResourcePermissions = resourcePermissions;
        }

        internal Role(string name, IList<ResourcePermission> resourcePermissions, string eTag)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            EnsureArg.IsNotNull(resourcePermissions, nameof(resourcePermissions));

            Name = name;
            ResourcePermissions = resourcePermissions;
            VersionTag = eTag;
        }

        [JsonProperty("name")]
        public virtual string Name { get;  set; }

        [JsonProperty("etag")]
        public virtual string VersionTag { get;  set; }

        [JsonProperty("resourcepermissions")]
        public virtual IList<ResourcePermission> ResourcePermissions { get;  set; } = new List<ResourcePermission>();

        public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult(Core.Resources.RoleNameEmpty);
            }

            if (ResourcePermissions.Count == 0)
            {
                yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, Core.Resources.ResourcePermissionEmpty));
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
