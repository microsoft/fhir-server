// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportTaskInputData
    {
        public string ResourceLocation { get; set; }

#pragma warning disable CA1056 // Uri properties should not be strings
        public string UriString { get; set; }
#pragma warning restore CA1056 // Uri properties should not be strings

#pragma warning disable CA1056 // Uri properties should not be strings
        public string BaseUriString { get; set; }
#pragma warning restore CA1056 // Uri properties should not be strings

        public string ResourceType { get; set; }

        public string TaskId { get; set; }

        public long BeginSequenceId { get; set; }

        public long EndSequenceId { get; set; }
    }
}
