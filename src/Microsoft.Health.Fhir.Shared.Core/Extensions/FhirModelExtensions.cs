// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class FhirModelExtensions
    {
        /// <summary>
        /// This method provides temporary compatibility while STU3/R4 compatibility is added
        /// </summary>
        public static void SetModelInfoProvider()
        {
            ModelInfoProvider.SetProvider(new VersionSpecificModelInfoProvider());
        }

        public static SearchParameterInfo ToInfo(this SearchParameter searchParam)
        {
            EnsureArg.IsNotNull(searchParam, nameof(searchParam));

            return new SearchParameterInfo(
                searchParam.Name,
                searchParam.Code,
                Enum.Parse<ValueSets.SearchParamType>(searchParam.Type?.ToString()),
                string.IsNullOrEmpty(searchParam.Url) ? null : new Uri(searchParam.Url),
                searchParam.Component?.Select(x => new SearchParameterComponentInfo(x.GetComponentDefinitionUri(), x.Expression)).ToArray(),
                searchParam.Expression,
                searchParam.Target?.Select(x => x?.ToString()).ToArray(),
                searchParam.Base?.Select(x => x?.ToString()).ToArray(),
                searchParam.Description?.Value);
        }

        public static ValueSets.SearchParamType ToValueSet(this SearchParamType searchParam)
        {
            return Enum.Parse<ValueSets.SearchParamType>(searchParam.ToString());
        }
    }
}
