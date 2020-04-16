// -------------------------------------------------------------------------------------------------
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
    /// A converter used to convert from <see cref="CodeableConcept"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class CodeableConceptNodeToTokenSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<TokenSearchValue>
    {
        public override string FhirNodeType { get; } = "CodeableConcept";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            // Based on spec: http://hl7.org/fhir/search.html#token,
            // CodeableConcept.text is searchable, but we will only create a dedicated entry for it
            // if it is different from the display text of one of its codings

            string text = value.Scalar("text") as string;
            bool conceptTextNeedsToBeAdded = !string.IsNullOrWhiteSpace(text);

            foreach (ITypedElement coding in value.Select("coding"))
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
                        conceptTextNeedsToBeAdded = !text.Equals(searchValue.Text, StringComparison.OrdinalIgnoreCase);
                    }

                    yield return searchValue;
                }
            }

            if (conceptTextNeedsToBeAdded)
            {
                yield return new TokenSearchValue(null, null, text);
            }
        }
    }
}
