// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    /// <summary>
    /// The configured RBAC roles.
    /// </summary>
    public sealed class RoleSet
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RoleSet"/> class.
        /// </summary>
        /// <param name="roles">The roles.</param>
        public RoleSet(IReadOnlyList<Role> roles)
        {
            Roles = EnsureArg.IsNotNull(roles, nameof(roles));
        }

        /// <summary>
        /// Gets the roles.
        /// </summary>
        public IReadOnlyList<Role> Roles { get; }
    }
}
