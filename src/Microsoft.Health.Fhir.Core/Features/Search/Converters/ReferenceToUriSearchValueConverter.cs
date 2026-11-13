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
    /// A converter used to convert from <see cref="ReferenceSearchValue"/> to <see cref="Uri"/>.
    /// </summary>
    public class ReferenceToUriSearchValueConverter() :
        FhirTypedElementToSearchValueConverter<UriSearchValue>("Reference")
    {
        protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
        {
            if (value.Scalar("reference") is not string reference)
            {
                yield break;
            }

            // Contained resources will not be searchable.
#if NET11_0_OR_GREATER
            if (reference.StartsWith('#', StringComparison.Ordinal)
#else
            if (reference.StartsWith('#')
#endif
                || reference.StartsWith("urn:", StringComparison.Ordinal))
            {
                yield break;
            }

            yield return new UriSearchValue(reference, true);
        }
    }
}
