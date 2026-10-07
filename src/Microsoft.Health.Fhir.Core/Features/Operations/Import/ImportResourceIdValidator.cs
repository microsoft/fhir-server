// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Provider-neutral validation of FHIR resource ids submitted through the $import operation.
    /// </summary>
    public static class ImportResourceIdValidator
    {
        /// <summary>
        /// Validates that a resource id conforms to the FHIR id requirements.
        /// </summary>
        /// <param name="resourceId">The resource id to validate.</param>
        /// <param name="policy">The resource id policy. Defaults to <see cref="ResourceIdPolicy.Standard"/>.</param>
        /// <exception cref="BadRequestException">Thrown when <paramref name="resourceId"/> is null, empty, whitespace-only, or does not match the FHIR id format.</exception>
        public static void Validate(string resourceId, ResourceIdPolicy policy = null)
        {
            policy ??= ResourceIdPolicy.Standard;

            if (string.IsNullOrWhiteSpace(resourceId) || !policy.IsValid(resourceId))
            {
                throw new BadRequestException(
                    $"Invalid resource id: '{resourceId ?? "null or empty"}'. " + string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, policy.MaxLength));
            }
        }
    }
}
