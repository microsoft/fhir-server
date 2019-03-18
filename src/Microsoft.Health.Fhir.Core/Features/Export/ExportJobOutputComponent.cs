// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobOutputComponent
    {
        public ExportJobOutputComponent(
            string type,
            int sequence,
            int count,
            long committedBytes)
        {
            EnsureArg.IsNotNullOrEmpty(type);

            Type = type;
            Sequence = sequence;
            Count = count;
            CommittedBytes = committedBytes;
        }

        public string Type { get; }

        public int Sequence { get; }

        public int Count { get; }

        public long CommittedBytes { get; }
    }
}
