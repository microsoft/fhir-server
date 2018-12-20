// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public class RoleNotFoundException : ControlPlaneException
    {
        public RoleNotFoundException(string name)
           : base(string.Format(Resources.RoleNotFound, name))
        {
            Debug.Assert(!string.IsNullOrEmpty(name), "Role identifier should not be empty");
        }
    }
}
