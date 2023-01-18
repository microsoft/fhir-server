// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

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

            converters = converters.ToList();
            var extensions = converters.GroupBy(x => x.SearchValueType).Select(group => new ExtensionConverter(group.Key, group.ToList())).ToArray();

            _converterDictionary = converters.Concat(extensions)
                .SelectMany(converter => converter.FhirTypes.Select(type => new { FhirType = type, converter.SearchValueType, Converter = converter }))
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

        internal class ExtensionConverter : ITypedElementToSearchValueConverter
        {
            private readonly List<ITypedElementToSearchValueConverter> _searchValueTypeConverters;

            public ExtensionConverter(Type searchValueSearchValueType, List<ITypedElementToSearchValueConverter> searchValueTypeConverters)
            {
                EnsureArg.IsNotNull(searchValueSearchValueType, nameof(searchValueSearchValueType));
                EnsureArg.IsNotNull(searchValueTypeConverters, nameof(searchValueTypeConverters));
                EnsureArg.HasItems(searchValueTypeConverters, nameof(searchValueTypeConverters));

                _searchValueTypeConverters = searchValueTypeConverters;
                SearchValueType = searchValueSearchValueType;
            }

            public IReadOnlyList<string> FhirTypes => new List<string> { "Extension" };

            public Type SearchValueType { get; }

            public IEnumerable<ISearchValue> ConvertTo(ITypedElement value)
            {
                if (value == null)
                {
                    return Enumerable.Empty<ISearchValue>();
                }

                var typed = value.Select("value").FirstOrDefault();
                var converter = _searchValueTypeConverters.FirstOrDefault(x => x.FhirTypes.Contains(typed?.InstanceType));

                // If the resource's extension type can't be converted to the user-specified search parameter type, we won't return any results.
                // This means reindexing will succeed but the search parameter will not pick up resources with extensions that have an incompatible type.
                if (converter == null)
                {
                    return Enumerable.Empty<ISearchValue>();
                }

                return converter.ConvertTo(typed);
            }
        }
    }
}
