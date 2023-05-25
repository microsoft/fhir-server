// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : FhirOperationDataStoreBase
    {
        public SqlServerFhirOperationDataStore(
            IQueueClient queueClient,
            ILoggerFactory loggerFactory)
            : base(queueClient, loggerFactory)
        {
        }
    }
}
