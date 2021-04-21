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
    public class FhirTypedElementToSearchValueConverterManager : ITypedElementToSearchValueConverterManager
    {
        private readonly Dictionary<(string fhirElementType, Type searchValueType), ITypedElementToSearchValueConverter> _converterDictionary;

        public FhirTypedElementToSearchValueConverterManager(IEnumerable<ITypedElementToSearchValueConverter> converters)
        {
            EnsureArg.IsNotNull(converters, nameof(converters));

            _converterDictionary = converters
                .SelectMany(converter => converter.FhirTypes.Select(type => new { FhirType=type, converter.SearchValueType, Converter=converter }))
                .ToDictionary(
                    converter => (converter.FhirType, converter.SearchValueType),
                    converter => converter.Converter);
        }

        /// <inheritdoc />
        public bool TryGetConverter(string fhirType, Type searchValueType, out ITypedElementToSearchValueConverter converter)
        {
            EnsureArg.IsNotNull(fhirType, nameof(fhirType));
            EnsureArg.IsNotNull(searchValueType, nameof(searchValueType));

            return _converterDictionary.TryGetValue((fhirType, searchValueType), out converter);
        }
    }
}
