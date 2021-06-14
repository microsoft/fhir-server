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

            var extensions = converters.GroupBy(x => x.SearchValueType).Select(group => new ExtensionConverter<int>(group.ToList())).ToArray();

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

        internal class ExtensionConverter<T> : ITypedElementToSearchValueConverter
        {
            private readonly IEnumerable<ITypedElementToSearchValueConverter> _underlying;
            private readonly Type _type;

            public ExtensionConverter(IEnumerable<ITypedElementToSearchValueConverter> underlying)
            {
                _underlying = underlying;
                _type = underlying.First().SearchValueType;
            }

            public IReadOnlyList<string> FhirTypes => new List<string> { "Extension" };

            public Type SearchValueType => _type;

            public IEnumerable<ISearchValue> ConvertTo(ITypedElement value)
            {
                if (value == null)
                {
                    return new List<ISearchValue>();
                }

                var typed = value.Select("value").FirstOrDefault();
                var converter = _underlying.Where(x => x.FhirTypes.Contains(typed.InstanceType)).First();
                return converter.ConvertTo(typed);
            }
        }
    }
}
