// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    public class AddressNodeToStringSearchValueConverter : FhirNodeToSearchValueTypeConverter<StringSearchValue>
    {
        public override string FhirNodeType { get; } = "Address";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            // http://hl7.org/fhir/patient.html recommends the following:
            // A server defined search that may match any of the string fields in the Address, including line, city, state, country, postalCode, and/or text.
            // we will do a basic search based on any of the address component for now. Details on localization will be handled later.

            var city = value.Scalar("city") as string;
            var country = value.Scalar("country") as string;
            var district = value.Scalar("district") as string;
            IEnumerable<ITypedElement> lines = value.Select("line");
            var postCode = value.Scalar("postalCode") as string;
            var state = value.Scalar("state") as string;
            var text = value.Scalar("text") as string;

            if (!string.IsNullOrWhiteSpace(city))
            {
                yield return new StringSearchValue(city);
            }

            if (!string.IsNullOrWhiteSpace(country))
            {
                yield return new StringSearchValue(country);
            }

            if (!string.IsNullOrWhiteSpace(district))
            {
                yield return new StringSearchValue(district);
            }

            foreach (var line in lines.AsStringValues())
            {
                yield return new StringSearchValue(line);
            }

            if (!string.IsNullOrWhiteSpace(postCode))
            {
                yield return new StringSearchValue(postCode);
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                yield return new StringSearchValue(state);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new StringSearchValue(text);
            }
        }
    }
}
