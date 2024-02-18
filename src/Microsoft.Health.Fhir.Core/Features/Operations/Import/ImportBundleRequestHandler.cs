// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Serialization;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// MediatR request handler. Called when the ImportController creates an Import job.
    /// </summary>
    public class ImportBundleRequestHandler : IRequestHandler<ImportBundleRequest, ImportBundleResponse>
    {
        private readonly IFhirDataStore _store;
        private readonly ILogger<ImportBundleRequestHandler> _logger;

        public ImportBundleRequestHandler(
            IFhirDataStore store,
            ILogger<ImportBundleRequestHandler> logger)
        {
            EnsureArg.IsNotNull(store, nameof(store));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _store = store;
            _logger = logger;
        }

        public async Task<ImportBundleResponse> Handle(ImportBundleRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            var lines = request.Bundle.Split(Environment.NewLine);
            return await Task.FromResult(new ImportBundleResponse(lines.Length));
        }
    }
}
