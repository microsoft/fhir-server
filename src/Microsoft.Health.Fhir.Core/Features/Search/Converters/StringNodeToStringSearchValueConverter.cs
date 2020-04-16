// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public class StringNodeToStringSearchValueConverter : FhirNodeToSearchValueTypeConverter<StringSearchValue>
    {
        public override string FhirNodeType { get; } = "string";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            if (value == null)
            {
                yield break;
            }

            EnsureArg.Matches(FhirNodeType, value.InstanceType, nameof(value));

            if (value.Value is string stringValue)
            {
                yield return new StringSearchValue(stringValue);
            }
        }
    }
}
