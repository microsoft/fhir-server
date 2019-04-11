// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    public class DestinationInfo
    {
        public DestinationInfo(string destinationType, string destinationConnectionString)
        {
            EnsureArg.IsNotNullOrWhiteSpace(destinationType);
            EnsureArg.IsNotNullOrWhiteSpace(destinationConnectionString);

            DestinationType = destinationType;
            DestinationConnectionString = destinationConnectionString;
        }

        [JsonConstructor]
        protected DestinationInfo()
        {
        }

        public string DestinationType { get; private set; }

        public string DestinationConnectionString { get; private set; }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
