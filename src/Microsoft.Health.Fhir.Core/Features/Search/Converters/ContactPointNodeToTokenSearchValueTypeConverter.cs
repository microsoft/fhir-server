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
    /// A converter used to convert from <see cref="ContactPoint"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class ContactPointNodeToTokenSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<TokenSearchValue>
    {
        public override string FhirNodeType { get; } = "ContactPoint";

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            string stringValue = value.Scalar("value") as string;
            string use = value.Scalar("use") as string;

            if (string.IsNullOrWhiteSpace(stringValue))
            {
                yield break;
            }

            yield return new TokenSearchValue(use, stringValue, null);
        }
    }
}
