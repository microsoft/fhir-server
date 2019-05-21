// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public static class SearchParameterNames
    {
        public static readonly string Id = "_id";

        public static readonly Uri IdUri = new Uri("http://hl7.org/fhir/SearchParameter/Resource-id");

        public static readonly string LastUpdated = "_lastUpdated";

        public static readonly Uri LastUpdatedUri = new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated");

        public static readonly string ResourceType = "_type";
    }
}
