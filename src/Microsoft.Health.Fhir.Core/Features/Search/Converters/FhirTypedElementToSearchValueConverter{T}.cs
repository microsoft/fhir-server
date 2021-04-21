// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public abstract class FhirTypedElementToSearchValueConverter<T> : ITypedElementToSearchValueConverter
    {
        protected FhirTypedElementToSearchValueConverter(params string[] fhirTypes)
        {
            EnsureArg.HasItems(fhirTypes, nameof(fhirTypes));

            FhirTypes = fhirTypes;
        }

        public IReadOnlyList<string> FhirTypes { get; }

        public Type SearchValueType { get; } = typeof(T);

        public IEnumerable<ISearchValue> ConvertTo(ITypedElement value)
        {
            if (value == null)
            {
                return Enumerable.Empty<ISearchValue>();
            }

            if (!FhirTypes.Contains(value.InstanceType))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            return Convert(value);
        }

        protected abstract IEnumerable<ISearchValue> Convert(ITypedElement value);
    }
}
