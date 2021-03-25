// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public class ConvertDataRequestHandler : IRequestHandler<ConvertDataRequest, ConvertDataResponse>
    {
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly IConvertDataEngine _convertDataEngine;
        private readonly ConvertDataConfiguration _convertDataConfiguration;

        public ConvertDataRequestHandler(
            IAuthorizationService<DataActions> authorizationService,
            IConvertDataEngine convertDataEngine,
            IOptions<ConvertDataConfiguration> convertDataConfiguration)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(convertDataEngine, nameof(convertDataEngine));
            EnsureArg.IsNotNull(convertDataConfiguration, nameof(convertDataConfiguration));

            _authorizationService = authorizationService;
            _convertDataEngine = convertDataEngine;
            _convertDataConfiguration = convertDataConfiguration.Value;
        }

        public async Task<ConvertDataResponse> Handle(ConvertDataRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request);

            if (await _authorizationService.CheckAccess(DataActions.ConvertData, cancellationToken) != DataActions.ConvertData)
            {
                throw new UnauthorizedFhirActionException();
            }

            return await _convertDataEngine.Process(request, cancellationToken);
        }
    }
}
