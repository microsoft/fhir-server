// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using EnsureThat;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Models
{
    [DebuggerDisplay("{Name}, Type: {Type}")]
    public class SearchParameterInfo : IEquatable<SearchParameterInfo>
    {
        public SearchParameterInfo(
            string name,
            string code,
            SearchParamType searchParamType,
            Uri url = null,
            IReadOnlyList<SearchParameterComponentInfo> components = null,
            string expression = null,
            IReadOnlyList<string> targetResourceTypes = null,
            IReadOnlyList<string> baseResourceTypes = null,
            string description = null)
            : this(name, code)
        {
            Url = url;
            Type = searchParamType;
            Component = components;
            Expression = expression;
            TargetResourceTypes = targetResourceTypes;
            BaseResourceTypes = baseResourceTypes;
            Description = description;
        }

        public SearchParameterInfo(string name, string code)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            EnsureArg.IsNotNullOrWhiteSpace(code, nameof(code));

            Name = name;
            Code = code;
        }

        public string Name { get; }

        public string Code { get; }

        public string Description { get; set; }

        public string Expression { get; }

        public IReadOnlyList<string> TargetResourceTypes { get; } = Array.Empty<string>();

        public IReadOnlyList<string> BaseResourceTypes { get; } = Array.Empty<string>();

        public Uri Url { get; }

        public SearchParamType Type { get; }

        /// <summary>
        /// Returns true if this parameter is enabled for searches
        /// </summary>
        public bool IsSearchable { get; set; } = true;

        /// <summary>
        /// Returns true if the system has the capability for indexing and searching for this parameter
        /// </summary>
        public bool IsSupported { get; set; } = true;

        /// <summary>
        /// Returns true if the search parameter resolves to more than one type (FhirString, FhirUri, etc...)
        /// but not all types are able to be indexed / searched
        /// </summary>
        public bool IsPartiallySupported { get; set; }

        /// <summary>
        /// The component definitions if this is a composite search parameter (<see cref="Type"/> is <see cref="SearchParamType.Composite"/>)
        /// </summary>
        public IReadOnlyList<SearchParameterComponentInfo> Component { get; }

        /// <summary>
        /// The resolved <see cref="SearchParameterInfo"/>s for each component if this is a composite search parameter (<see cref="Type"/> is <see cref="SearchParamType.Composite"/>)
        /// </summary>
        public IReadOnlyList<SearchParameterInfo> ResolvedComponents { get; set; } = Array.Empty<SearchParameterInfo>();

        public bool Equals([AllowNull] SearchParameterInfo other)
        {
            if (other == null)
            {
                return false;
            }

            if (Url != other.Url)
            {
                return false;
            }

            if (Url == null)
            {
                if (!Code.Equals(other.Code, StringComparison.OrdinalIgnoreCase) ||
                    Type != other.Type ||
                    Expression != other.Expression)
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SearchParameterInfo);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Url?.GetHashCode(),
                Code?.GetHashCode(StringComparison.OrdinalIgnoreCase),
                Type.GetHashCode(),
                Expression?.GetHashCode(StringComparison.OrdinalIgnoreCase));
        }
    }
}
