// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.ControlPlane.Core.Features.Rbac;

namespace Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Rbac
{
    public static class CosmosIdentityProviderExtensions
    {
        internal static IdentityProvider ToIdentityProvider(this CosmosIdentityProvider cosmosIdentityProvider)
        {
            EnsureArg.IsNotNull(cosmosIdentityProvider);

            return new IdentityProvider(
                cosmosIdentityProvider.Name,
                cosmosIdentityProvider.Authority,
                cosmosIdentityProvider.Audience,
                cosmosIdentityProvider.ETag);
        }
    }
}
