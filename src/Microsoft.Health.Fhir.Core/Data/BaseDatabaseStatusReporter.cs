// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Data
{
    public abstract class BaseDatabaseStatusReporter : IDatabaseStatusReporter
    {
        private DatabaseAvailability _databaseAvailability;

        protected BaseDatabaseStatusReporter()
        {
            _databaseAvailability = DatabaseAvailability.Available;
        }

        public abstract bool IsCustomerManagedKeyException(Exception exception);

        public abstract Task<bool> IsCustomerManagedKeyProperlySetAsync(CancellationToken cancellationToken);

#pragma warning disable CA1024 // Use properties where appropriate. Justification: Prefer to keep this as a method for consistency with the other methods in this interface.
        public DatabaseAvailability GetDatabaseAvailability()
        {
            return _databaseAvailability;
        }
#pragma warning restore CA1024

        public void SetDatabaseAvailability(DatabaseAvailability availability)
        {
            _databaseAvailability = availability;
        }
    }
}
