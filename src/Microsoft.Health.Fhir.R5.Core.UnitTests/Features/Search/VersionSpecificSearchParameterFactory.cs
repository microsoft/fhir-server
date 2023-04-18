// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    internal static class VersionSpecificSearchParameterFactory
    {
        public static SearchParameter CreateSearchParameter(string url, string code, string[] resourceTypes, string name = null, string expression = null, SearchParamType? type = SearchParamType.Token)
        {
            var baseTypes = new List<AllResourceTypes?>();

            foreach (var resourceType in resourceTypes)
            {
                baseTypes.Add(Enum.Parse<AllResourceTypes>(resourceType));
            }

            return new SearchParameter()
            {
                Url = url,
                Name = name,
                Code = code,
                Type = type,
                Base = baseTypes,
                Expression = expression,
            };
        }
    }
}
