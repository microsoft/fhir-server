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
            var parameter = new Parameter()
            {
                Name = "test",
                DateValue = DateTime.Now,
                NumberValue = 0,
                LongValue = 0,
                CharValue = "test",
                BooleanValue = false,
                UpdatedOn = DateTime.Now,
                UpdatedBy = "default",
            };
            return Task.FromResult<Parameter>(null);
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
