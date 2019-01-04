// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class Role : IValidatableObject
    {
        public string Name { get; set; }

        public virtual string Version { get; set; }

        public IList<ResourcePermission> ResourcePermissions { get; internal set; } = new List<ResourcePermission>();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult("Role name empty");
            }

            if (ResourcePermissions.Count == 0)
            {
                yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, "Resource Permission empty {0}", Name));
            }
            else
            {
                foreach (ResourcePermission permission in ResourcePermissions)
                {
                    if (permission.Actions.Count == 0)
                    {
                        yield return new ValidationResult(string.Format(CultureInfo.InvariantCulture, "Role resource with no resource action {0}", Name));
                    }
                }
            }
        }
    }
}
