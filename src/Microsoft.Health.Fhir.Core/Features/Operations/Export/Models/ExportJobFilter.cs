// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.Models
{
    public class ExportJobFilter
    {
        public ExportJobFilter(string resourceType, IList<Tuple<string, string>> parameters)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(parameters, nameof(parameters));

            ResourceType = resourceType;
            Parameters = parameters;
        }

        [JsonConstructor]
        public ExportJobFilter()
        {
        }

        [JsonProperty(JobRecordProperties.ResourceType)]
        public string ResourceType { get; private set; }

        [JsonProperty(JobRecordProperties.SearchParams)]
        public IList<Tuple<string, string>> Parameters { get; private set; }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return GetHashCode() == obj.GetHashCode();
        }

        public override int GetHashCode()
        {
            var paramHash = default(HashCode);
            foreach (var param in Parameters)
            {
                paramHash.Add(param.Item1);
                paramHash.Add(param.Item2);
            }

            paramHash.Add(ResourceType);
            return paramHash.ToHashCode();
        }
    }
}
