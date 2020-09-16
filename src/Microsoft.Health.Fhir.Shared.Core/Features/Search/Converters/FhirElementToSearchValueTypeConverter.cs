// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// Provides mechanisms to convert from <typeparamref name="TFhirElement"/> to a list of <see cref="ISearchValue"/>.
    /// </summary>
    /// <typeparam name="TFhirElement">The FHIR element type.</typeparam>
    /// <typeparam name="TSearchValue">The search value type that this converter creates</typeparam>
    public abstract class FhirElementToSearchValueTypeConverter<TFhirElement, TSearchValue> : IFhirElementToSearchValueTypeConverter
        where TFhirElement : Element
        where TSearchValue : ISearchValue
    {
        /// <summary>
        /// Gets the FHIR element type that this converter supports.
        /// </summary>
        public Type FhirElementType { get; } = typeof(TFhirElement);

        public Type SearchValueType => typeof(TSearchValue);

        /// <summary>
        /// Converts the FHIR element to a list of <see cref="ISearchValue"/>.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A list of <see cref="ISearchValue"/>.</returns>
        public IEnumerable<ISearchValue> ConvertTo(object value)
        {
            if (value == null)
            {
                return Enumerable.Empty<ISearchValue>();
            }

            EnsureArg.IsOfType(value, typeof(TFhirElement), nameof(value));

            return (IEnumerable<ISearchValue>)ConvertTo((TFhirElement)value);
        }

        /// <summary>
        /// Converts the FHIR element to a list of <see cref="ISearchValue"/>.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A list of <see cref="ISearchValue"/>.</returns>
        protected abstract IEnumerable<TSearchValue> ConvertTo(TFhirElement value);
    }
}
