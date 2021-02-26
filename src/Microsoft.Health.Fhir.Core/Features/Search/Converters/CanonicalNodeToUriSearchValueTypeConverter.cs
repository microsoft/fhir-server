// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Canonical"/> to a list of <see cref="UriSearchValue"/>.
    /// </summary>
    public class CanonicalNodeToUriSearchValueTypeConverter : FhirNodeToSearchValueTypeConverter<UriSearchValue>
    {
        public CanonicalNodeToUriSearchValueTypeConverter()
            : base("canonical")
        {
        }

        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            /* For more information see: https://www.hl7.org/fhir/search.html#uri
             *
             * "Note that for uri parameters that refer to the Canonical URLs of the conformance and knowledge resources
             * (e.g. StructureDefinition, ValueSet, PlanDefinition etc), servers SHOULD support searching by canonical references,
             * and SHOULD support automatically detecting a |[version] portion as part of the search parameter, and interpreting that
             * portion as a search on the version"
             *
             * Because this is a URI search parameter, not a reference, the Canonical components will be separated and ignored
             */

            yield return new UriSearchValue(value.Value.ToString(), true);
        }
    }
}
