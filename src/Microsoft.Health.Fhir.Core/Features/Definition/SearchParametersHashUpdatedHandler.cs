// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    public class SearchParametersHashUpdatedHandler : INotificationHandler<SearchParametersHashUpdated>
    {
        private readonly SearchParameterDefinitionManager _searchParameterDefinitionManager;

        public SearchParametersHashUpdatedHandler(SearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public Task Handle(SearchParametersHashUpdated notification, CancellationToken cancellationToken)
        {
            _searchParameterDefinitionManager.SearchParametersHash = notification.HashValue;
            return Task.CompletedTask;
        }
    }
}
