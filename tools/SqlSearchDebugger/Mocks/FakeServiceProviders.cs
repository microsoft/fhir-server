// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;

namespace SqlSearchDebugger.Mocks;

class FakeScopeProvider<T> : IScopeProvider<T> where T : class
{
    private readonly T _instance;

    public FakeScopeProvider(T instance) => _instance = instance;

    public IScoped<T> Invoke() => new FakeScoped<T>(_instance);
}

class FakeScoped<T> : IScoped<T> where T : class
{
    public FakeScoped(T value) => Value = value;

    public T Value { get; }

    public void Dispose() { }
}

class FakeSearchParameterComparer : ISearchParameterComparer<SearchParameterInfo>
{
    public int Compare(SearchParameterInfo? x, SearchParameterInfo? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        return string.Compare(x.Url?.ToString(), y.Url?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public int CompareBase(IEnumerable<string> x, IEnumerable<string> y) => 0;

    public int CompareComponent(IEnumerable<(string definition, string expression)> x, IEnumerable<(string definition, string expression)> y) => 0;

    public int CompareExpression(string x, string y, bool isQuantity) => string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
}
