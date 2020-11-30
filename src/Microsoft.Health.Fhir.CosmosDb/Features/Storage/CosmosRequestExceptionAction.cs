// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR.Pipeline;
using Microsoft.Azure.Cosmos;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosRequestExceptionAction<TRequest, TException> : IRequestExceptionAction<TRequest, TException>
        where TException : Exception
    {
        private readonly ICosmosResponseProcessor _processor;

        public CosmosRequestExceptionAction(ICosmosResponseProcessor processor)
        {
            EnsureArg.IsNotNull(processor, nameof(processor));

            _processor = processor;
        }

        public Task Execute(TRequest request, TException exception, CancellationToken cancellationToken)
        {
            if (exception is CosmosException && (exception.InnerException is FhirException || exception.InnerException is MicrosoftHealthException))
            {
                // The SDK wraps exceptions we throw in handlers with a CosmosException.
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }
            else if (exception is CosmosException cosmosException)
            {
                _processor.ProcessErrorResponse(cosmosException.StatusCode, cosmosException.Headers, cosmosException.Message);
            }

            return Task.CompletedTask;
        }
    }
}
