// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Identifier"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class IdentifierToTokenSearchValueConverter : FhirTypedElementToSearchValueConverter<TokenSearchValue>
    {
        public IdentifierToTokenSearchValueConverter()
            : base("Identifier")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string stringValue = value.Scalar("value") as string;
            string system = value.Scalar("system") as string;
            string type = value.Scalar("type.text") as string;
            if (!string.IsNullOrEmpty(system) && !string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(stringValue))
            {
                // Based on spec: http://hl7.org/fhir/search.html#token,
                // the text for identifier is specified by Identifier.type.text.
                yield return new TokenSearchValue(system, stringValue, type);
            }

            if (string.IsNullOrEmpty(stringValue))
            {
                yield break;
            }

            var codingCollection = value.Select("type.coding");
            foreach (var coding in codingCollection)
            {
                string codingCode = coding.Scalar("code") as string;
                string codingSystem = coding.Scalar("system") as string;
                if (!string.IsNullOrEmpty(codingCode) && !string.IsNullOrEmpty(codingSystem))
                {
                    yield return new IdentifierOfTypeSearchValue(codingSystem, codingCode, stringValue);
                }
            }
        }
    }
}
