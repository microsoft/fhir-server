// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Thrown when a resource id in a search, either an <c>_id</c> value or the id of a reference, does not satisfy the active resource id policy.
    /// Callers that compile mandatory authorization constraints distinguish this type from ordinary invalid input.
    /// The message never contains the submitted value.
    /// </summary>
    public sealed class ResourceIdentityQueryException : BadRequestException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResourceIdentityQueryException"/> class.
        /// </summary>
        /// <param name="maximumLength">The maximum resource id length of the active policy.</param>
        public ResourceIdentityQueryException(int maximumLength)
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
