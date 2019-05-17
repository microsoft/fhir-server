// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class SearchParameterInfo
    {
        public SearchParameterInfo(
            string name,
            Uri url,
            string searchParamType,
            ICollection<SearchParameterComponentInfo> components)
            : this(name)
        {
            EnsureArg.IsNotNull(url, nameof(url));
            EnsureArg.IsNotNullOrEmpty(searchParamType, nameof(searchParamType));

            Url = url;
            Type = searchParamType;
            Component = components;
        }

        public SearchParameterInfo(string name)
        {
            EnsureArg.IsNotNullOrWhiteSpace(name, nameof(name));

            Name = name;
        }

        public string Name { get; }

        public string Code { get; }

        public Uri Url { get; }

        public string Type { get; }

        public ICollection<SearchParameterComponentInfo> Component { get; }
    }
}
