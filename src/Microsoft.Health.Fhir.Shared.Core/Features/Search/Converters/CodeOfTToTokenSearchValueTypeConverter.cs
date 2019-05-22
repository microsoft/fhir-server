// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// A converter used to convert from <see cref="Code{T}"/> to a list of <see cref="TokenSearchValue"/>.
    /// </summary>
    public class CodeOfTToTokenSearchValueTypeConverter : IFhirElementToSearchValueTypeConverter
    {
        public Type FhirElementType => typeof(Code<>);

        public Type SearchValueType => typeof(TokenSearchValue);

        public IEnumerable<ISearchValue> ConvertTo(object value)
        {
            if (value == null)
            {
                yield break;
            }

            Type type = value.GetType();

            EnsureArg.IsTrue(type.IsGenericType && type.GetGenericTypeDefinition() == FhirElementType, nameof(value));

            ISystemAndCode systemAndCode = (ISystemAndCode)value;

            yield return new TokenSearchValue(systemAndCode.System, systemAndCode.Code, null);
        }
    }
}
