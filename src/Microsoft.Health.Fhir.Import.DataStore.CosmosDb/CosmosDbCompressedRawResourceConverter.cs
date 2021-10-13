// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Import.DataStore.CosmosDb
{
    public class CosmosDbCompressedRawResourceConverter : ICompressedRawResourceConverter
    {
        public Task<string> ReadCompressedRawResource(Stream compressedResourceStream)
        {
            // throw new System.NotImplementedException();
            return Task.FromResult(string.Empty);
        }

        public void WriteCompressedRawResource(Stream outputStream, string rawResource)
        {
            // throw new System.NotImplementedException();
        }
    }
}
