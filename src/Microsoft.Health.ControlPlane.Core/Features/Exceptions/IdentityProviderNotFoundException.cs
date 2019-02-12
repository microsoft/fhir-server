// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public class IdentityProviderNotFoundException : ControlPlaneException
    {
        public IdentityProviderNotFoundException(string name)
            : base(ValidateAndFormatMessage(Resources.IdentityProviderNotFound, name))
        {
        }
    }
}
