// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class MergeOptions
    {
        public MergeOptions(bool enlistTransaction = true)
        {
            EnlistInTransaction = enlistTransaction;
        }

        public static MergeOptions Default { get; private set; } = new MergeOptions();

        public bool EnlistInTransaction { get; private set; }
    }
}
