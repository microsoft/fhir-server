// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Thrown when a reference in a search cannot be compared by the data store without loss.
    /// Callers that compile mandatory authorization constraints distinguish this type from ordinary invalid input.
    /// </summary>
    public sealed class ReferenceStorageQueryException : BadRequestException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceStorageQueryException"/> class.
        /// </summary>
        /// <param name="failure">The data store capacity failure for the reference.</param>
        public ReferenceStorageQueryException(ReferenceStorageCapacityException failure)
            : base(EnsureArg.IsNotNull(failure, nameof(failure)).Message)
        {
            Component = failure.Component;
            Capacity = failure.Capacity;
        }

        /// <summary>
        /// Gets the reference component that cannot be compared.
        /// </summary>
        public FieldName Component { get; }

        /// <summary>
        /// Gets the maximum number of supported characters the data store holds for <see cref="Component"/>.
        /// </summary>
        public int Capacity { get; }
    }
}
