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
    /// A converter used to convert from <see cref="HumanName"/> to a list of <see cref="StringSearchValue"/>.
    /// </summary>
    public class HumanNameToStringSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<HumanName, StringSearchValue>
    {
        protected override IEnumerable<StringSearchValue> ConvertTo(HumanName value)
        {
            // https://www.hl7.org/fhir/STU3/patient.html recommends the following:
            // A server defined search that may match any of the string fields in the HumanName, including family, give, prefix, suffix, suffix, and/or text
            // we will do a basic search based on family or given or prefix or suffix or text for now. Details on localization will be handled later.
            foreach (string given in value.Given ?? Enumerable.Empty<string>())
            {
                yield return new StringSearchValue(given);
            }

            if (!string.IsNullOrWhiteSpace(value.Family))
            {
                yield return new StringSearchValue(value.Family);
            }

            foreach (string prefix in value.Prefix ?? Enumerable.Empty<string>())
            {
                yield return new StringSearchValue(prefix);
            }

            foreach (string suffix in value.Suffix ?? Enumerable.Empty<string>())
            {
                yield return new StringSearchValue(suffix);
            }

            if (!string.IsNullOrWhiteSpace(value.Text))
            {
                yield return new StringSearchValue(value.Text);
            }
        }
    }
}
