// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using FluentValidation.Results;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Validation
{
    /// <summary>
    /// Validates every resource identity a resource carries: its own id and the id of each typed reference,
    /// including references that are not indexed by any search parameter.
    /// </summary>
    public interface IResourceIdentityValidator
    {
        /// <summary>
        /// Validates the identities carried by a resource.
        /// </summary>
        /// <param name="resource">The resource to validate.</param>
        /// <returns>The failures. Each failure message omits the submitted value.</returns>
        IReadOnlyList<ValidationFailure> Validate(ResourceElement resource);
    }
}
