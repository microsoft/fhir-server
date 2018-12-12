// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public interface IRbacService
    {
        IdentityProvider GetIdentityProviderAsync(string name, CancellationToken cancellationToken);
    }
}
