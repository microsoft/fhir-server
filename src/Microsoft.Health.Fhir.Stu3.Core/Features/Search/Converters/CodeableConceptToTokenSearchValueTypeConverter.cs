// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="CodeableConcept"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class CodeableConceptToTokenSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<CodeableConcept, TokenSearchValue>
    {
        protected override IEnumerable<TokenSearchValue> ConvertTo(CodeableConcept value)
        {
            // Based on spec: http://hl7.org/fhir/STU3/search.html#token,
            // CodeableConcept.text is searchable, but we will only create a dedicated entry for it
            // if it is different from the display text of one of its codings

            bool conceptTextNeedsToBeAdded = !string.IsNullOrWhiteSpace(value.Text);

            if (value.Coding != null)
            {
                foreach (Coding coding in value.Coding)
                {
                    if (coding == null)
                    {
                        continue;
                    }

                    TokenSearchValue searchValue = coding.ToTokenSearchValue();

                    if (searchValue != null)
                    {
                        if (conceptTextNeedsToBeAdded)
                        {
                            conceptTextNeedsToBeAdded = !value.Text.Equals(searchValue.Text, StringComparison.OrdinalIgnoreCase);
                        }

                        yield return searchValue;
                    }
                }
            }

            if (conceptTextNeedsToBeAdded)
            {
                yield return new TokenSearchValue(null, null, value.Text);
            }
        }
    }
}
