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
    /// <summary>
    /// A converter used to convert from <see cref="HumanName"/> to a list of <see cref="StringSearchValue"/>.
    /// </summary>
    public class HumanNameNodeToStringSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<StringSearchValue>
    {
        public override string FhirNodeType { get; } = "HumanName";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            IEnumerable<ITypedElement> givenNames = value.Select("given");
            IEnumerable<ITypedElement> prefixes = value.Select("prefix");
            IEnumerable<ITypedElement> suffixes = value.Select("suffix");
            var family = value.Scalar("family") as string;
            var text = value.Scalar("text") as string;

            // https://www.hl7.org/fhir/patient.html recommends the following:
            // A server defined search that may match any of the string fields in the HumanName, including family, give, prefix, suffix, suffix, and/or text
            // we will do a basic search based on family or given or prefix or suffix or text for now. Details on localization will be handled later.
            foreach (var given in givenNames.AsStringValues())
            {
                yield return new StringSearchValue(given);
            }

            if (!string.IsNullOrWhiteSpace(family))
            {
                yield return new StringSearchValue(family);
            }

            foreach (var prefix in prefixes.AsStringValues())
            {
                yield return new StringSearchValue(prefix);
            }

            foreach (var suffix in suffixes.AsStringValues())
            {
                yield return new StringSearchValue(suffix);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return new StringSearchValue(text);
            }
        }
    }
}
