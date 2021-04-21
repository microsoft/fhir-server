// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SqlSearchParameters
    {
        public const string ResourceSurrogateIdParameterName = "_resourceSurrogateId";

        public static readonly Uri ResourceSurrogateIdUri = new Uri("http://fhirserverforazure.microsoft.com/fhir/SearchParameter/Resource-surrogateid");

        public static readonly SearchParameterInfo ResourceSurrogateIdParameter = new SearchParameterInfo(ResourceSurrogateIdParameterName, ResourceSurrogateIdParameterName, SearchParamType.Number, ResourceSurrogateIdUri, null);

        public const string PrimaryKeyParameterName = "_primaryKey";

        public static readonly Uri PrimaryKeyUri = new Uri("http://fhirserverforazure.microsoft.com/fhir/SearchParameter/Resource-primaryKey");

        public static readonly SearchParameterInfo PrimaryKeyParameter = new SearchParameterInfo(PrimaryKeyParameterName, PrimaryKeyParameterName, SearchParamType.Number, PrimaryKeyUri, null);
    }
}
