// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Core.Configs;
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
        /// <param name="useLongResourceIds">Whether ids up to 128 characters are allowed.</param>
        /// <exception cref="BadRequestException">Thrown when <paramref name="resourceId"/> is null, empty, whitespace-only, or does not match the FHIR id format.</exception>
        public static void Validate(string resourceId, bool useLongResourceIds = false)
        {
            if (string.IsNullOrWhiteSpace(resourceId) ||
                !ResourceIdValidation.IsValid(resourceId, useLongResourceIds))
            {
                int maxLength = ResourceIdValidation.GetMaxLength(useLongResourceIds);
                throw new BadRequestException(
                    $"Invalid resource id: '{resourceId ?? "null or empty"}'. " + string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, maxLength));
            }
        }
    }
}
