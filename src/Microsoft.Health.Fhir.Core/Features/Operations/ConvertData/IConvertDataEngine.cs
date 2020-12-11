// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public interface IConvertDataEngine
    {
        public Task<ConvertDataResponse> Process(ConvertDataRequest convertRequest, CancellationToken cancellationToken);
    }
}
