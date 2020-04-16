// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public interface IFhirNodeToSearchValueTypeConverter
    {
        string FhirNodeType { get; }

        Type SearchValueType { get; }

        IEnumerable<ISearchValue> ConvertTo(ITypedElement value);
    }
}
