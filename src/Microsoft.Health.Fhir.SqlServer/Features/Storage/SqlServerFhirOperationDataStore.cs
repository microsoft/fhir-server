// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        public Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            IReadOnlyCollection<ExportJobOutcome> returnValue = new List<ExportJobOutcome>();
            return Task.FromResult(returnValue);
        }

        public Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
