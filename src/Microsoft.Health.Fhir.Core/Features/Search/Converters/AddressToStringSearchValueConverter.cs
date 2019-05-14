// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Address"/> to a list of <see cref="StringSearchValue"/>.
    /// </summary>
    public class AddressToStringSearchValueConverter : FhirElementToSearchValueTypeConverter<Address, StringSearchValue>
    {
        protected override IEnumerable<StringSearchValue> ConvertTo(Address value)
        {
            // http://hl7.org/fhir/STU3/patient.html recommends the following:
            // A server defined search that may match any of the string fields in the Address, including line, city, state, country, postalCode, and/or text.
            // we will do a basic search based on any of the address component for now. Details on localization will be handled later.
            if (!string.IsNullOrWhiteSpace(value.City))
            {
                yield return new StringSearchValue(value.City);
            }

            if (!string.IsNullOrWhiteSpace(value.Country))
            {
                yield return new StringSearchValue(value.Country);
            }

            if (!string.IsNullOrWhiteSpace(value.District))
            {
                yield return new StringSearchValue(value.District);
            }

            foreach (string line in value.Line ?? Enumerable.Empty<string>())
            {
                yield return new StringSearchValue(line);
            }

            if (!string.IsNullOrWhiteSpace(value.PostalCode))
            {
                yield return new StringSearchValue(value.PostalCode);
            }

            if (!string.IsNullOrWhiteSpace(value.State))
            {
                yield return new StringSearchValue(value.State);
            }

            if (!string.IsNullOrWhiteSpace(value.Text))
            {
                yield return new StringSearchValue(value.Text);
            }
        }
    }
}
