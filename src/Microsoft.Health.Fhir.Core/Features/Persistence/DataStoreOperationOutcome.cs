// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public sealed class DataStoreOperationOutcome
    {
        public DataStoreOperationOutcome(UpsertOutcome outcome)
        {
            // Outcome can be null as the result of a DELETE operation.

            UpsertOutcome = outcome;
            Exception = null;
        }

        public DataStoreOperationOutcome(FhirException exception)
        {
            EnsureArg.IsNotNull(exception, nameof(exception));

            Exception = exception;
            UpsertOutcome = null;
        }

        public bool IsOperationSuccessful
        {
            get { return Exception == null; }
        }

        public FhirException Exception { get; }

        public UpsertOutcome UpsertOutcome { get; }
    }
}
