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
    /// A converter used to convert from <see cref="Markdown"/> to a list of <see cref="StringSearchValue"/>.
    /// </summary>
    public class MarkdownToStringSearchValueTypeConverter : FhirElementToSearchValueTypeConverter<Markdown, StringSearchValue>
    {
        protected override IEnumerable<StringSearchValue> ConvertTo(Markdown value)
        {
            if (value.Value == null)
            {
                yield break;
            }

            yield return new StringSearchValue(value.Value);
        }
    }
}
