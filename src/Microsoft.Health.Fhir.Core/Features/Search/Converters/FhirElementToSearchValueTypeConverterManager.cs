// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// Provides mechanisms to access FHIR element type converter.
    /// </summary>
    public class FhirElementToSearchValueTypeConverterManager : IFhirElementToSearchValueTypeConverterManager, IStartable
    {
        private Dictionary<Type, IFhirElementToSearchValueTypeConverter> _converterDictionary = new Dictionary<Type, IFhirElementToSearchValueTypeConverter>();

        /// <inheritdoc />
        public void Start()
        {
            Type type = GetType();
            Type interfaceType = typeof(IFhirElementToSearchValueTypeConverter);

            // Load all concrete converter types.
            IEnumerable<Type> converterTypes = type.Assembly.GetTypes()
                .Where(t =>
                    !t.IsAbstract &&
                    interfaceType.IsAssignableFrom(t) &&
                    t.Namespace.StartsWith(type.Namespace, StringComparison.Ordinal));

            // Creates and caches the instance of the converter.
            foreach (Type converterType in converterTypes)
            {
                var converter = (IFhirElementToSearchValueTypeConverter)Activator.CreateInstance(converterType);

                _converterDictionary.Add(converter.FhirElementType, converter);
            }
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
