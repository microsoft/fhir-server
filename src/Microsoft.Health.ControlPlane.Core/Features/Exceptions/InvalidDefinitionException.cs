// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public class InvalidDefinitionException : ControlPlaneException
    {
        public InvalidDefinitionException(string message, IEnumerable<ValidationResult> validationResult)
            : base(message, validationResult.Select(vr => vr.ErrorMessage))
        {
        }
    }
}
