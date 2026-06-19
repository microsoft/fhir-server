// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "This is a collection of search parameters")]
    public class SearchParameterCollection
    {
        private readonly Dictionary<string, SearchParameter> _parametersByCode;
        private readonly Dictionary<int, SearchParameter> _parametersById;

        public SearchParameterCollection()
        {
            _parametersByCode = new Dictionary<string, SearchParameter>(StringComparer.OrdinalIgnoreCase);
            _parametersById = new Dictionary<int, SearchParameter>();
        }

        public SearchParameterCollection(IEnumerable<SearchParameter> parameters)
            : this()
        {
            foreach (var parameter in parameters)
            {
                Add(parameter);
            }
        }

        public int Count => _parametersById.Count;

        public IEnumerable<SearchParameter> All => _parametersById.Values;

        public void Add(SearchParameter parameter)
        {
            ArgumentNullException.ThrowIfNull(parameter);

            _parametersByCode[parameter.Code] = parameter;
            _parametersById[parameter.Id] = parameter;
        }

        public SearchParameter? GetByCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            if (code.Contains(':', StringComparison.OrdinalIgnoreCase))
            {
                code = code.Split(':', 2, StringSplitOptions.None)[0];
            }

            _parametersByCode.TryGetValue(code, out var parameter);
            return parameter;
        }

        public SearchParameter? GetById(int id)
        {
            _parametersById.TryGetValue(id, out var parameter);
            return parameter;
        }

        public string? GetParameterType(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            var parameter = GetByCode(code);
            return parameter?.Type;
        }
    }
}
