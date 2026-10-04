// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Health.Fhir.Core.Features.Persistence;

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
        /// <param name="maxLength">The maximum number of characters allowed in a resource id.</param>
        /// <exception cref="BadRequestException">Thrown when <paramref name="resourceId"/> is null, empty, whitespace-only, or does not match the FHIR id format.</exception>
        public static void Validate(string resourceId, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(resourceId) ||
                !Regex.IsMatch(resourceId, $"^[A-Za-z0-9\\-\\.]{{1,{maxLength}}}$"))
            {
                throw new BadRequestException(
                    $"Invalid resource id: '{resourceId ?? "null or empty"}'. " + string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, maxLength));
            }
        }
    }
}
