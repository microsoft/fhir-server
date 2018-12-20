// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public class IdentityProviderNotFoundException : ControlPlaneException
    {
        public IdentityProviderNotFoundException(string name)
            : base(string.Format(Resources.IdentityProviderNotFound, name))
        {
            Debug.Assert(!string.IsNullOrEmpty(name), "Identity provider name should not be empty");
        }
    }
}
