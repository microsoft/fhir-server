// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core
{
    /// <summary>
    /// Provides extension methods to FHIR objects.
    /// </summary>
    public static class FhirExtensions
    {
        /// <summary>
        /// Checks whether the <see cref="Coding"/> instance is empty or not.
        /// </summary>
        /// <param name="coding">The coding instance to check.</param>
        /// <returns><c>true</c> if the instance is empty; <c>false</c> otherwise.</returns>
        public static bool IsEmpty(this Coding coding)
        {
            EnsureArg.IsNotNull(coding, nameof(coding));

            return string.IsNullOrEmpty(coding.System) && string.IsNullOrEmpty(coding.Code);
        }

        /// <summary>
        /// Checks whether the <see cref="Quantity"/> instance is empty or not.
        /// </summary>
        /// <param name="quantity">The quantity instance to check.</param>
        /// <returns><c>true</c> if the instance is empty; <c>false</c> otherwise.</returns>
        public static bool IsEmpty(this Quantity quantity)
        {
            EnsureArg.IsNotNull(quantity, nameof(quantity));

            return quantity.Value == null;
        }

        /// <summary>
        /// Checks whether the <see cref="ResourceReference"/> instance is empty or not.
        /// </summary>
        /// <param name="reference">The reference instance to check.</param>
        /// <returns><c>true</c> if the instance is empty; <c>false</c> otherwise.</returns>
        public static bool IsEmpty(this ResourceReference reference)
        {
            EnsureArg.IsNotNull(reference, nameof(reference));

            return string.IsNullOrWhiteSpace(reference.Reference);
        }

        /// <summary>
        /// Checks whether the resource referenced by <paramref name="reference"/> is type of <paramref name="resourceType"/>.
        /// </summary>
        /// <param name="reference">The reference to check.</param>
        /// <param name="resourceType">The resource type to match.</param>
        /// <returns><c>true</c> if the resource referenced by <paramref name="reference"/> is type of <paramref name="resourceType"/>; <c>false</c> otherwise.</returns>
        public static bool IsReferenceTypeOf(this ResourceReference reference, FHIRAllTypes resourceType)
        {
            EnsureArg.IsNotNull(reference, nameof(reference));

            // TODO: This does not work with external reference.
            return reference?.Reference?.StartsWith(ModelInfo.FhirTypeToFhirTypeName(resourceType), StringComparison.Ordinal) ?? false;
        }

        /// <summary>
        /// Converts a <see cref="Hl7.Fhir.Rest.SortOrder"/> to a <see cref="SortOrder"/>.
        /// </summary>
        /// <param name="input">The <see cref="Hl7.Fhir.Rest.SortOrder"/></param>
        /// <returns>The <see cref="SortOrder"/></returns>
        public static SortOrder ToCoreSortOrder(this Hl7.Fhir.Rest.SortOrder input)
        {
            return input == Hl7.Fhir.Rest.SortOrder.Ascending ? SortOrder.Ascending : SortOrder.Descending;
        }
    }
}
