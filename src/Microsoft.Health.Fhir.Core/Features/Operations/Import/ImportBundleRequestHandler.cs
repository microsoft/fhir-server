// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
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
        private readonly IAuthorizationService<DataActions> _authorizationService;

        public ImportBundleRequestHandler(
            IFhirDataStore store,
            ILogger<ImportBundleRequestHandler> logger,
            IAuthorizationService<DataActions> authorizationService)
        {
            _store = EnsureArg.IsNotNull(store, nameof(store));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _authorizationService = EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
        }

        public async Task<ImportBundleResponse> Handle(ImportBundleRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            if (await _authorizationService.CheckAccess(DataActions.Write, cancellationToken) != DataActions.Write)
            {
                throw new UnauthorizedFhirActionException();
            }

            var input = request.Resources.Select(_ => new ResourceWrapperOperation(_.ResourceWrapper, true, false, null, false, false, null)).ToList();
            await _store.MergeAsync(input, new MergeOptions(false), cancellationToken);
            return await Task.FromResult(new ImportBundleResponse(request.Resources.Count));
        }
    }
}
