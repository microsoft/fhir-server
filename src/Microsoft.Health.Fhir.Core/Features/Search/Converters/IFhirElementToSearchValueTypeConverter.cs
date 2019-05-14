// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// Provides mechanisms to convert from FHIR element to a list of <see cref="ISearchValue"/>.
    /// </summary>
    public interface IFhirElementToSearchValueTypeConverter
    {
        Type FhirElementType { get; }

        Type SearchValueType { get; }

        IEnumerable<ISearchValue> ConvertTo(object value);
    }
}
