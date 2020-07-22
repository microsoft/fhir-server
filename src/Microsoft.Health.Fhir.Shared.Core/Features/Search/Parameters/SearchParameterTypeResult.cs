// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using Hl7.Fhir.Introspection;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    internal class SearchParameterTypeResult
    {
        public SearchParameterTypeResult(ClassMapping classMapping, SearchParamType searchParamType, string path, Uri definition)
        {
            ClassMapping = classMapping;
            SearchParamType = searchParamType;
            Path = path;
            Definition = definition;

            FhirNodeType = ((FhirTypeAttribute)ClassMapping.NativeType.GetCustomAttribute(typeof(FhirTypeAttribute))).Name;
        }

        public ClassMapping ClassMapping { get; set; }

        public string FhirNodeType { get; }

        public SearchParamType SearchParamType { get; }

        public string Path { get; set; }

        public Uri Definition { get; }
    }
}
