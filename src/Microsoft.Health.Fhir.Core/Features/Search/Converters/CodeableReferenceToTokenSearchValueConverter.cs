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
    /// A converter used to convert from <see cref="CodeableReference "/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class CodeableReferenceToTokenSearchValueConverter : FhirTypedElementToSearchValueConverter<TokenSearchValue>
    {
        private readonly CodeableConceptToTokenSearchValueConverter _converter;

        public CodeableReferenceToTokenSearchValueConverter()
            : base("CodeableReference")
        {
            _converter = new CodeableConceptToTokenSearchValueConverter();
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var concept = value.Scalar("concept") as ITypedElement;

            if (concept == null)
            {
                return Enumerable.Empty<ISearchValue>();
            }

            return _converter.ConvertTo(concept);
        }
    }
}
