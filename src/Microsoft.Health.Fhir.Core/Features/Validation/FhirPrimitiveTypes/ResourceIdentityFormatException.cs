// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;

namespace Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes
{
    /// <summary>
    /// Thrown when a resource id, or the id component of a reference, does not satisfy the active <see cref="ResourceIdPolicy"/>.
    /// The message never contains the submitted value.
    /// </summary>
    public sealed class ResourceIdentityFormatException : FormatException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceIdentityFormatException"/> class.
        /// </summary>
        /// <param name="maximumLength">The maximum resource id length of the active policy.</param>
        public ResourceIdentityFormatException(int maximumLength)
            : base(string.Format(CultureInfo.InvariantCulture, Core.Resources.IdRequirements, maximumLength))
        {
            MaximumLength = maximumLength;
        }

        /// <summary>
        /// Gets the maximum resource id length of the active policy.
        /// </summary>
        public int MaximumLength { get; }
    }
}
