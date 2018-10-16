// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="CodeableConcept"/> to a list of <see cref="ISearchValue"/>.
    /// </summary>
    public class CodeableConceptToSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<CodeableConcept>
    {
        protected override IEnumerable<ISearchValue> ConvertTo(CodeableConcept value)
        {
            // Based on spec: http://hl7.org/fhir/STU3/search.html#token,
            // CodeableConcept.text is searchable.
            if (!string.IsNullOrWhiteSpace(value.Text))
            {
                yield return new TokenSearchValue(null, null, value.Text);
            }

            if (value.Coding?.Count == 0)
            {
                yield break;
            }

            foreach (Coding coding in value.Coding)
            {
                if (coding == null)
                {
                    continue;
                }

                TokenSearchValue searchValue = coding.ToTokenSearchValue();

                if (searchValue != null)
                {
                    yield return searchValue;
                }
            }
        }
    }
}
