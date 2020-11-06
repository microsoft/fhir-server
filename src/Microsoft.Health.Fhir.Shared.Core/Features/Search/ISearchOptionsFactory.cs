// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public interface ISearchOptionsFactory
    {
        (SearchOptions searchOptions, IReadOnlyList<OperationOutcome> warnings) Create(string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters);

        (SearchOptions searchOptions, IReadOnlyList<OperationOutcome> warnings) Create(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters);
    }
}
