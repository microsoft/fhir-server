﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Range"/> to a list of <see cref="NumberSearchValue"/>.
    /// </summary>
    public class RangeToNumberSearchValueConverter : FhirTypedElementToSearchValueConverter<NumberSearchValue>
    {
        public RangeToNumberSearchValueConverter()
            : base("Range")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var lowValue = (decimal?)value.Scalar("low.value");
            var highValue = (decimal?)value.Scalar("high.value");

            if (lowValue.HasValue || highValue.HasValue)
            {
                yield return new NumberSearchValue(lowValue, highValue);
            }
        }
    }
}
