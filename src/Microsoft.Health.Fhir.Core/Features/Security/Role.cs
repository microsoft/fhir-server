// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    public class Role : IValidatableObject
    {
        public virtual string Name { get; set; }

        public virtual string Version { get; set; }

        public virtual IList<ResourcePermission> ResourcePermissions { get; internal set; } = new List<ResourcePermission>();

        public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
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
