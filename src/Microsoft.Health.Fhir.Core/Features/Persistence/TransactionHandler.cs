// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Persistence
{
    public class TransactionHandler : ITransactionHandler
    {
        private readonly IFhirDataStore _fhirDataStore;

        public TransactionHandler(IFhirDataStore fhirDataStore)
        {
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));

            _fhirDataStore = fhirDataStore;
        }

        public void BeginTransactionScope()
        {
            _fhirDataStore.BeginTransactionScope();
        }

        public void CompleteTransactionScope()
        {
            _fhirDataStore.CompleteTransactionScope();
        }

        public void Dispose()
        {
            _fhirDataStore.Dispose();
        }
    }
}
