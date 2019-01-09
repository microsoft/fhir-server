// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    public static class CosmosRoleExtensions
    {
        public static Role ToRole(this CosmosRole cosmosRole)
        {
            return new Role(
                cosmosRole.Name,
                cosmosRole.ResourcePermissions,
                cosmosRole.ETag.Trim('"'));
        }

        public static List<Role> ToRoleList(this IEnumerable<CosmosRole> cosmosRoleList)
        {
            List<Role> roleList = new List<Role>();

            if (cosmosRoleList != null)
            {
                foreach (CosmosRole r in cosmosRoleList)
                {
                    Role role = new Role(
                         r.Name,
                         r.ResourcePermissions,
                         r.ETag.Trim('"'));

                    roleList.Add(role);
                }
            }

            return roleList;
        }
    }
}
