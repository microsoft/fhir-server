// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class DataConvertEngine : IDataConvertEngine
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task<DataConvertResponse> Process(DataConvertRequest convertRequest, CancellationToken cancellationToken)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            // This is just a shell for now. Will be completed in the future
            return new DataConvertResponse("Not implemented yet.");
        }
    }
}
