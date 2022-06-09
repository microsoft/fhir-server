// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public interface IAnonymizerFactory
    {
        public Task<IAnonymizer> CreateAnonymizerAsync(ExportJobRecord exportJobRecord, CancellationToken cancellationToken);
    }
}
