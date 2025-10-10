// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public class ScopeRestriction : IEquatable<ScopeRestriction>
    {
        public ScopeRestriction(string resource, DataActions allowedAction, string user, SearchParams searchParameters = null)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
            AllowedDataAction |= allowedAction;
            User = user;
            SearchParameters = searchParameters;
        }

        public string Resource { get; }

        // Indictes whether access to a resource should be restricted to the User, Patient or System levels
        public string User { get; }

        // read, write or both
        public DataActions AllowedDataAction { get; }

        // Finer-grained resource constraints using search parameters for SMART V2 compliance
        public SearchParams SearchParameters { get; }

        public bool Equals(ScopeRestriction other)
        {
            if (other == null)
            {
                return false;
            }

            bool searchParamsEqual;
            if (SearchParameters == null && other.SearchParameters == null)
            {
                searchParamsEqual = true;
            }
            else if (SearchParameters != null && other.SearchParameters != null)
            {
                // Assume SearchParameters is enumerable like IEnumerable<KeyValuePair<string, string>>
                var dict1 = SearchParameters.Parameters.Select(t => new KeyValuePair<string, string>(t.Item1, t.Item2)).ToList();
                var dict2 = other.SearchParameters.Parameters.Select(t => new KeyValuePair<string, string>(t.Item1, t.Item2)).ToList();

                var sorted1 = dict1
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(kvp => kvp.Value, StringComparer.Ordinal)
                    .ToList();

                var sorted2 = dict2
                    .OrderBy(kvp => kvp.Key, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(kvp => kvp.Value, StringComparer.Ordinal)
                    .ToList();

                searchParamsEqual = sorted1.SequenceEqual(sorted2, new KeyValuePairComparer());
            }
            else
            {
                searchParamsEqual = false;
            }

            return string.Equals(Resource, other.Resource, StringComparison.Ordinal) &&
                   string.Equals(User, other.User, StringComparison.Ordinal) &&
                   AllowedDataAction == other.AllowedDataAction &&
                   searchParamsEqual;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ScopeRestriction);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Resource, User, AllowedDataAction);
        }
    }
}
