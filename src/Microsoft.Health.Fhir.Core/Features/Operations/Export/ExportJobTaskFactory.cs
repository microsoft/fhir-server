// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    /// <summary>
    /// Provides mechanism to create a new export job task.
    /// </summary>
    public class ExportJobTaskFactory : IExportJobTaskFactory
    {
        private readonly IFhirOperationsDataStore _fhirOperationsDataStore;
        private readonly ILoggerFactory _loggerFactory;

        public ExportJobTaskFactory(
            IFhirOperationsDataStore fhirOperationsDataStore,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(fhirOperationsDataStore, nameof(fhirOperationsDataStore));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _fhirOperationsDataStore = fhirOperationsDataStore;
            _loggerFactory = loggerFactory;
        }

        /// <inheritdoc />
        public Task Create(ExportJobRecord exportJobRecord, WeakETag weakETag, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            var exportJobTask = new ExportJobTask(
                exportJobRecord,
                weakETag,
                _fhirOperationsDataStore,
                _loggerFactory.CreateLogger<ExportJobTask>());

            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(async () => await exportJobTask.ExecuteAsync(cancellationToken));
            }
        }
    }
}
