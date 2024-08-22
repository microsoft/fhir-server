// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Parameters;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosParameterStore : IParameterStore
    {
        public Task<Parameter> GetParameter(string name, CancellationToken cancellationToken)
        {
            return Task.FromResult(new Parameter());
        }

        public Task SetParameter(Parameter parameter, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public void ResetCache()
        {
        }
    }
}
