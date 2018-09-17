// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage.Security
{
    public static class CosmosRoleExtensions
    {
        public static Role ToRole(this CosmosRole cosmosRole)
        {
            return new Role(cosmosRole.Name, cosmosRole.ResourcePermissions) { Version = cosmosRole.ETag.Trim('"') };
        }
    }
}
