// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public class InvalidDefinitionException : ControlPlaneException
    {
        public InvalidDefinitionException(string name, List<string> issues)
            : base(name, issues)
        {
        }
    }
}
