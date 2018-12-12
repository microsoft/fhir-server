// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.ControlPlane.Core.Features.Rbac;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.ControlPlane;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage
{
    public static class CosmosIdentityProviderExtensions
    {
        public static IdentityProvider ToIdentityProvider(this CosmosIdentityProvider cosmosIdentityProvider)
        {
            return new IdentityProvider(cosmosIdentityProvider.Name, cosmosIdentityProvider.Authority, cosmosIdentityProvider.Audience)
            {
                Version = cosmosIdentityProvider.ETag.Trim('"'),
            };
        }
    }
}
