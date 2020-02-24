// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Introspection;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterTypeResult
    {
        public SearchParameterTypeResult(ClassMapping classMapping, SearchParamType searchParamType, string path, Uri definition)
        {
            ClassMapping = classMapping;
            SearchParamType = searchParamType;
            Path = path;
            Definition = definition;
        }

        public ClassMapping ClassMapping { get; set; }

        public SearchParamType SearchParamType { get; }

        public string Path { get; set; }

        public Uri Definition { get; }
    }
}
