// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers
{
    public class SearchParameterWrapper
    {
        private readonly Lazy<string> _url;
        private readonly Lazy<IReadOnlyList<ITypedElement>> _component;
        private readonly Lazy<string> _expression;
        private readonly Lazy<IReadOnlyList<string>> _base;
        private readonly Lazy<string> _name;
        private readonly Lazy<string> _code;
        private readonly Lazy<IReadOnlyList<string>> _target;
        private readonly Lazy<string> _description;
        private Lazy<string> _type;

        public SearchParameterWrapper(ITypedElement searchParameter)
        {
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.Is(KnownResourceTypes.SearchParameter, searchParameter.InstanceType, StringComparison.Ordinal, nameof(searchParameter));

            _name = new Lazy<string>(() => searchParameter.Scalar("name")?.ToString());
            _code = new Lazy<string>(() => searchParameter.Scalar("code")?.ToString());
            _description = new Lazy<string>(() => searchParameter.Scalar("description")?.ToString());
            _url = new Lazy<string>(() => searchParameter.Scalar("url")?.ToString());
            _expression = new Lazy<string>(() => searchParameter.Scalar("expression")?.ToString());
            _type = new Lazy<string>(() => searchParameter.Scalar("type")?.ToString());

            _base = new Lazy<IReadOnlyList<string>>(() => searchParameter.Select("base")?.AsStringValues().ToArray());
            _component = new Lazy<IReadOnlyList<ITypedElement>>(() => searchParameter.Select("component")?.ToArray());
            _target = new Lazy<IReadOnlyList<string>>(() => searchParameter.Select("target")?.AsStringValues().ToArray());
        }

        public string Name => _name.Value;

        public string Code => _code.Value;

        public string Description => _description.Value;

#pragma warning disable CA1056 // URI-like properties should not be strings
        public string Url => _url.Value;
#pragma warning restore CA1056 // URI-like properties should not be strings

        public string Type => _type.Value;

        public string Expression => _expression.Value;

        public IReadOnlyList<string> Base => _base.Value;

        public IReadOnlyList<string> Target => _target.Value;

        public IReadOnlyList<ITypedElement> Component => _component.Value;
    }
}
