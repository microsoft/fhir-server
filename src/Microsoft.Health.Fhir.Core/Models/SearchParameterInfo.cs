// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class SearchParameterInfo
    {
        public SearchParameterInfo(
            string name,
            Uri url = null,
            string searchParamType = null,
            IReadOnlyList<SearchParameterComponentInfo> components = null,
            string expression = null,
            IReadOnlyCollection<string> targetResourceTypes = null)
            : this(
                name,
                url,
                Enum.TryParse<SearchParamType>(searchParamType, out var type) ? (SearchParamType?)type : null,
                components,
                expression,
                targetResourceTypes)
        {
        }

        public SearchParameterInfo(
            string name,
            Uri url = null,
            SearchParamType? searchParamType = null,
            IReadOnlyList<SearchParameterComponentInfo> components = null,
            string expression = null,
            IReadOnlyCollection<string> targetResourceTypes = null)
            : this(name)
        {
            Url = url;
            Type = searchParamType;
            Component = components;
            Expression = expression;
            TargetResourceTypes = targetResourceTypes;
        }

        public SearchParameterInfo(string name)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));

            Name = name;
        }

        public string Name { get; }

        public string Code { get; }

        public string Expression { get; }

        public IReadOnlyCollection<string> TargetResourceTypes { get; } = Array.Empty<string>();

        public Uri Url { get; }

        public SearchParamType? Type { get; }

        public IReadOnlyList<SearchParameterComponentInfo> Component { get; }
    }
}
