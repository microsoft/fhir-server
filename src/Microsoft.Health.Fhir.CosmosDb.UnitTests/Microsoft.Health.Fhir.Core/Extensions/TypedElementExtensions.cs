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

namespace Microsoft.Health.Fhir.Core.Extensions
{
    internal static class TypedElementExtensions
    {
        public static TokenSearchValue ToTokenSearchValue(this ITypedElement coding)
        {
            EnsureArg.IsNotNull(coding, nameof(coding));

            string system = coding.Scalar("system") as string;
            string code = coding.Scalar("code") as string;
            string display = coding.Scalar("display") as string;

            if (!string.IsNullOrWhiteSpace(system) ||
                !string.IsNullOrWhiteSpace(code) ||
                !string.IsNullOrWhiteSpace(display))
            {
                return new TokenSearchValue(system, code, display);
            }

            return null;
        }

        public static IEnumerable<string> AsStringValues(this IEnumerable<ITypedElement> elements)
        {
            if (elements == null)
            {
                return Enumerable.Empty<string>();
            }

            return elements.Select(x => x.Value as string).Where(x => !string.IsNullOrWhiteSpace(x));
        }

        public static string GetStringScalar(this ITypedElement resource, string propertyName)
        {
            EnsureArg.IsNotNull(propertyName, nameof(propertyName));

            return resource.Scalar(propertyName)?.ToString();
        }
    }
}
