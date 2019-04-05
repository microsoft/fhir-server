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
    public class FhirElementToSearchValueTypeConverterManager : IFhirElementToSearchValueTypeConverterManager
    {
        private Dictionary<Type, IFhirElementToSearchValueTypeConverter> _converterDictionary = new Dictionary<Type, IFhirElementToSearchValueTypeConverter>();

        public FhirElementToSearchValueTypeConverterManager(IEnumerable<IFhirElementToSearchValueTypeConverter> converters)
        {
            EnsureArg.IsNotNull(converters, nameof(converters));

            _converterDictionary = converters.ToDictionary(
                converter => converter.FhirElementType,
                converter => converter);
        }

        /// <inheritdoc />
        public bool TryGetConverter(Type fhirElementType, out IFhirElementToSearchValueTypeConverter converter)
        {
            EnsureArg.IsNotNull(fhirElementType, nameof(fhirElementType));

            if (fhirElementType.IsGenericType)
            {
                fhirElementType = fhirElementType.GetGenericTypeDefinition();
            }

            return _converterDictionary.TryGetValue(fhirElementType, out converter);
        }
    }
}
