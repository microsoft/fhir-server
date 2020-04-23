// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// Provides mechanisms to access FHIR element type converter.
    /// </summary>
    public class FhirNodeToSearchValueTypeConverterManager : IFhirNodeToSearchValueTypeConverterManager
    {
        private readonly Dictionary<(string fhirElementType, Type searchValueType), IFhirNodeToSearchValueTypeConverter> _converterDictionary;

        public FhirNodeToSearchValueTypeConverterManager(IEnumerable<IFhirNodeToSearchValueTypeConverter> converters)
        {
            EnsureArg.IsNotNull(converters, nameof(converters));

            _converterDictionary = converters.ToDictionary(
                converter => (converter.FhirNodeType, converter.SearchValueType),
                converter => converter);
        }

        /// <inheritdoc />
        public bool TryGetConverter(string fhirElementType, Type searchValueType, out IFhirNodeToSearchValueTypeConverter converter)
        {
            EnsureArg.IsNotNull(fhirElementType, nameof(fhirElementType));

            return _converterDictionary.TryGetValue((fhirElementType, searchValueType), out converter);
        }
    }
}
