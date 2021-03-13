// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Models
{
    [DebuggerDisplay("{Name}, Type: {Type}")]
    public class SearchParameterInfo : IEquatable<SearchParameterInfo>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterInfo"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="code">The code.</param>
        /// <param name="searchParamType">The search parameter type.</param>
        /// <param name="url">The url./</param>
        /// <param name="components">The search parameter components info list.</param>
        /// <param name="expression">The expression.</param>
        /// <param name="targetResourceTypes">The list of target resource type.</param>
        /// <param name="baseResourceTypes">The list of base resource types.</param>
        /// <param name="description">The description.</param>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchParameterInfo"/> class.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="code">The code.</param>
        public SearchParameterInfo(string name, string code)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));
            EnsureArg.IsNotNullOrWhiteSpace(code, nameof(code));

            Name = name;
            Code = code;
        }

        internal SearchParameterInfo(SearchParameterWrapper wrapper)
        {
            var components = wrapper.Component
                .Select(x => new SearchParameterComponentInfo(
                    new Uri(GetComponentDefinition(x)),
                    x.Scalar("expression")?.ToString()))
                .ToArray();

            SearchParamType searchParamType = EnumUtility.ParseLiteral<SearchParamType>(wrapper.Type)
                .GetValueOrDefault();

            Name = wrapper.Name;
            Code = wrapper.Code;
            Type = searchParamType;
            Url = new Uri(wrapper.Url);
            Expression = wrapper.Expression;
            Description = wrapper.Description;
            Component = components;
            TargetResourceTypes = wrapper.Target;
            BaseResourceTypes = wrapper.Base;

            string GetComponentDefinition(ITypedElement component)
            {
                // In Stu3 the Url is under 'definition.reference'
                return component.Scalar("definition.reference")?.ToString() ??
                   component.Scalar("definition")?.ToString();
            }
        }

        /// <summary>
        /// Gets the name of the search paramter.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the search parameter code.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets or sets the description of search parameter.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets the expression.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// Gets the list of target resource types.
        /// </summary>
        public IReadOnlyList<string> TargetResourceTypes { get; } = Array.Empty<string>();

        /// <summary>
        /// Gets the list of base resource types.
        /// </summary>
        public IReadOnlyList<string> BaseResourceTypes { get; } = Array.Empty<string>();

        /// <summary>
        /// Gets the url.
        /// </summary>
        public Uri Url { get; }

        /// <summary>
        /// Gets the search parameter type.
        /// </summary>
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
        /// The status of the search parameters use for sorting
        /// </summary>
        public SortParameterStatus SortStatus { get; set; }

        /// <summary>
        /// The component definitions if this is a composite search parameter (<see cref="Type"/> is <see cref="SearchParamType.Composite"/>)
        /// </summary>
        public IReadOnlyList<SearchParameterComponentInfo> Component { get; }

        /// <summary>
        /// The resolved <see cref="SearchParameterInfo"/>s for each component if this is a composite search parameter (<see cref="Type"/> is <see cref="SearchParamType.Composite"/>)
        /// </summary>
        public IReadOnlyList<SearchParameterInfo> ResolvedComponents { get; set; } = Array.Empty<SearchParameterInfo>();

        /// <summary>
        /// Checks if the current SearchParameterInfo object is equal to the passed SearchParameterInfo object.
        /// </summary>
        /// <param name="other">The search parameter info object.</param>
        /// <returns>True, if both the search parameter infos are equal, else false.</returns>
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

        /// <summary>
        /// Implements the Equals method of the <see cref="IEquatable{SearchParameterInfo}"/> interface.
        /// </summary>
        /// <param name="obj">The object.</param>
        /// <returns>True, if both the search parameter infos are equal, else false.</returns>
        public override bool Equals(object obj)
        {
            return Equals(obj as SearchParameterInfo);
        }

        /// <summary>
        /// Gets the hash code.
        /// </summary>
        /// <returns>The hash code.</returns>
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
