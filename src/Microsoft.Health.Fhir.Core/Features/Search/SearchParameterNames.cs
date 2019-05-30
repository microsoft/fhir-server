// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public static class SearchParameterNames
    {
        public const string Id = "_id";

        public static readonly Uri IdUri = new Uri("http://hl7.org/fhir/SearchParameter/Resource-id");

        public const string LastUpdated = "_lastUpdated";

        public static readonly Uri LastUpdatedUri = new Uri("http://hl7.org/fhir/SearchParameter/Resource-lastUpdated");

        public const string ResourceType = "_type";

        public static readonly Uri ResourceTypeUri = new Uri("http://hl7.org/fhir/SearchParameter/Resource-type");
    }
}
