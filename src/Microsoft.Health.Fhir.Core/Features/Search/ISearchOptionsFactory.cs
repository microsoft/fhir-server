﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface ISearchOptionsFactory
    {
        SearchOptions Create(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false);

        SearchOptions Create(
            string compartmentType,
            string compartmentId,
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            bool isAsyncOperation = false,
            bool useSmartCompartmentDefinition = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false);
    }
}
