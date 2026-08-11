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
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Models
{
    [DebuggerDisplay("{Name}, Type: {Type}")]
    public class SearchParameterInfo : IEquatable<SearchParameterInfo>
    {
        public static readonly SearchParameterInfo ResourceTypeSearchParameter = new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType, SearchParamType.Token, SearchParameterNames.ResourceTypeUri, null, "Resource.type().name", null);

        public static readonly SearchParameterInfo ScoreSearchParameter = new SearchParameterInfo(SearchParameterNames.Score, SearchParameterNames.Score, SearchParamType.Special);

        public SearchParameterInfo(
            string name,
            string code,
            SearchParamType searchParamType,
            Uri url = null,
            IReadOnlyList<SearchParameterComponentInfo> components = null,
            string expression = null,
            IReadOnlyList<string> targetResourceTypes = null,
            IReadOnlyList<string> baseResourceTypes = null,
            string description = null,
            VectorSearchParameterConfig vectorConfig = null,
            string definitionStatus = null)
            : this(name, code)
        {
            Url = url;
            Type = searchParamType;
            Component = components;
            Expression = expression;
            TargetResourceTypes = targetResourceTypes;
            BaseResourceTypes = baseResourceTypes;
            Description = description;
            VectorConfig = vectorConfig;
            DefinitionStatus = definitionStatus;
        }

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
            DefinitionStatus = wrapper.Status;
            Component = components;
            TargetResourceTypes = wrapper.Target;
            BaseResourceTypes = wrapper.Base;
            VectorConfig = wrapper.VectorConfig;

            string GetComponentDefinition(ITypedElement component)
            {
                // In Stu3 the Url is under 'definition.reference'
                return component.Scalar("definition.reference")?.ToString() ??
                   component.Scalar("definition")?.ToString();
            }
        }

        public string Name { get; }

        public string Code { get; }

        public string Description { get; set; }

        /// <summary>
        /// Gets the publication status declared by the FHIR SearchParameter definition.
        /// </summary>
        public string DefinitionStatus { get; }

        public string Expression { get; }

        public IReadOnlyList<string> TargetResourceTypes { get; } = Array.Empty<string>();

        public IReadOnlyList<string> BaseResourceTypes { get; } = Array.Empty<string>();

        public Uri Url { get; }

        public SearchParamType Type { get; set; }

        /// <summary>
        /// Gets vector-specific configuration when this definition is a vector SearchParameter.
        /// </summary>
        public VectorSearchParameterConfig VectorConfig { get; }

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
        /// Current state of the search parameter in the search parameter registry
        /// </summary>
        public SearchParameterStatus SearchParameterStatus { get; set; }

        /// <summary>
        /// Returns true if this parameter is defined by the FHIR specification (out-of-the-box) and should not be modified or deleted by users
        /// </summary>
        public bool IsSystemDefined { get; set; }

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
            // When Url is non-null, Equals compares only by Url.
            // GetHashCode must be consistent: include only fields used by Equals.
            if (Url != null)
            {
                return Url.GetHashCode();
            }

            return HashCode.Combine(
                Code?.GetHashCode(StringComparison.OrdinalIgnoreCase),
                Type.GetHashCode(),
                Expression?.GetHashCode(StringComparison.OrdinalIgnoreCase));
        }
    }
}
