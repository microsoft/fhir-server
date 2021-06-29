// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="CodeableReference "/> to a list of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public class CodeableReferenceToReferenceSearchValueConverter : FhirTypedElementToSearchValueConverter<ReferenceSearchValue>
    {
        private readonly ResourceReferenceToReferenceSearchValueConverter _referenceSearchValueParser;

        public CodeableReferenceToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
            : base("CodeableReference")
        {
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

            _referenceSearchValueParser = new ResourceReferenceToReferenceSearchValueConverter(referenceSearchValueParser);
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            var reference = value.Scalar("reference") as ITypedElement;

            if (reference == null)
            {
                return Enumerable.Empty<ISearchValue>();
            }

            return _referenceSearchValueParser.ConvertTo(reference);
        }
    }
}
