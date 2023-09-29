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

        public static bool operator ==(ExportJobFilter left, ExportJobFilter right)
        {
            return object.Equals(left, right);
        }

        public static bool operator !=(ExportJobFilter left, ExportJobFilter right)
        {
            return !object.Equals(left, right);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            ExportJobFilter other = (ExportJobFilter)obj;
            return ResourceType == other.ResourceType && AreParametersEqual(Parameters, other.Parameters);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 23) + (ResourceType?.GetHashCode(StringComparison.InvariantCulture) ?? 0);

                foreach (var param in Parameters)
                {
                    hash = (hash * 23) + (param?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }

        private static bool AreParametersEqual(IList<Tuple<string, string>> a, IList<Tuple<string, string>> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Item1 != b[i].Item1 || a[i].Item2 != b[i].Item2)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
