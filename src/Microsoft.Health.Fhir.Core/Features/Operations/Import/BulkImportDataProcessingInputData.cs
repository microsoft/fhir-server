// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class BulkImportDataProcessingInputData
    {
        public string ResourceLocation { get; set; }

        public string ResourceType { get; set; }

        public long StartSurrogateId { get; set; }

        public long EndSurrogateId { get; set; }
    }
}
