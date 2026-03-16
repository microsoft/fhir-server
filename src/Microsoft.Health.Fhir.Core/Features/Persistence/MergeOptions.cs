// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class MergeOptions
    {
        /// <summary>
        /// C# transactions are not desired for majority of workloads and, if required, should be set explicitly
        /// </summary>
        public MergeOptions(bool enlistTransaction = false, bool ensureAtomicOperations = false)
        {
            EnlistInTransaction = enlistTransaction;
            EnsureAtomicOperations = ensureAtomicOperations;
        }

        public static MergeOptions Default { get; private set; } = new MergeOptions();

        public bool EnlistInTransaction { get; private set; }

        public bool EnsureAtomicOperations { get; private set;  }
    }
}
