// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
                cosmosRole.ETag);
        }
    }
}
