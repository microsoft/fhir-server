#nullable enable

// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Parameters
{
    public class Parameter
    {
        public string? Id { get; set; }

        public string? CharValue { get; set; }

        public double? NumberValue { get; set; }

        public long? LongValue { get; set; }

        public DateTime? DateValue { get; set; }

#pragma warning disable CA1819 // Properties should not return arrays. The array is used to store binary data.
        public byte[]? BinaryValue { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
    }
}
