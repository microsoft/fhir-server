// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Parameters
{
    public interface IParameterStore
    {
        public Parameter GetParameter(string name);

        public Task SetParameter(Parameter parameter, CancellationToken cancellationToken);

        public void ResetCache();
    }
}
