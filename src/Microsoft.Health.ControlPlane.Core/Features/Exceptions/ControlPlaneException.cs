// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public abstract class ControlPlaneException : Exception
    {
        protected ControlPlaneException(string message)
            : base(message)
        {
        }
    }
}
