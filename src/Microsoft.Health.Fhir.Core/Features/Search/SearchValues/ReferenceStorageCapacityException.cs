// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Globalization;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Thrown when a component of a reference cannot be indexed or compared by the data store without loss.
    /// The message names the component and its capacity and never contains the submitted value.
    /// </summary>
    public sealed class ReferenceStorageCapacityException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceStorageCapacityException"/> class.
        /// </summary>
        /// <param name="component">The reference component that cannot be represented, either <see cref="FieldName.ReferenceResourceId"/> or <see cref="FieldName.ReferenceBaseUri"/>.</param>
        /// <param name="capacity">The maximum number of supported characters the data store holds for <paramref name="component"/>.</param>
        public ReferenceStorageCapacityException(FieldName component, int capacity)
            : base(FormatMessage(component, capacity))
        {
            Component = component;
            Capacity = capacity;
        }

        /// <summary>
        /// Gets the reference component that cannot be represented.
        /// </summary>
        public FieldName Component { get; }

        /// <summary>
        /// Gets the maximum number of supported characters the data store holds for <see cref="Component"/>.
        /// </summary>
        public int Capacity { get; }

        private static string FormatMessage(FieldName component, int capacity)
        {
            string format = component switch
            {
                FieldName.ReferenceResourceId => Core.Resources.ReferenceResourceIdNotStorable,
                FieldName.ReferenceBaseUri => Core.Resources.ReferenceBaseUriNotStorable,
                _ => throw new ArgumentOutOfRangeException(nameof(component)),
            };

            return string.Format(CultureInfo.InvariantCulture, format, capacity);
        }
    }
}
