// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public class ResourceSearchParameterStatus
    {
        public Uri Uri { get; set; }

        public SearchParameterStatus Status { get; set; }

        public bool IsPartiallySupported { get; set; }

        public SortParameterStatus SortStatus { get; set; }

        public DateTimeOffset LastUpdated { get; set; }
    }
}
