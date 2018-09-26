// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class Role : IValidatableObject
    {
        public Role()
        {
        }

        [JsonConstructor]
        public Role(string name, IReadOnlyList<ResourcePermission> resourcePermissions)
        {
            Name = name;
            ResourcePermissions = resourcePermissions;
        }

        public string Name { get; set; }

        public virtual string Version { get; set; }

        public IReadOnlyList<ResourcePermission> ResourcePermissions { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult(Core.Resources.RoleNameEmpty);
            }

            if (ResourcePermissions == null || ResourcePermissions.Count == 0)
            {
                yield return new ValidationResult(Core.Resources.ResourcePermissionEmpty);
            }

            if (ResourcePermissions != null && ResourcePermissions.Count > 1)
            {
                yield return new ValidationResult(string.Format(Core.Resources.RoleResourcePermissionNotSupported, 1, Name));
            }

            foreach (ResourcePermission permission in ResourcePermissions)
            {
                if (permission.Actions == null || permission.Actions.Count == 0)
                {
                    yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, Core.Resources.RoleResourcePermissionWithNoAction, Name));
                }
            }
        }
    }
}
