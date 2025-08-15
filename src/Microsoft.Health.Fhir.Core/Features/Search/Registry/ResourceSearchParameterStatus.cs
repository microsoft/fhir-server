// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
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

        /// <summary>
        /// RowVersion for optimistic concurrency control.
        /// Only relevant for SQL Server implementation.
        /// </summary>
        [SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "RowVersion for optimistic concurrency")]
        public byte[] RowVersion { get; set; }
    }
}
