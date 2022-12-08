// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using NSubstitute;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions;

public class XUnitLogger<T> : ILogger<T>, IDisposable
{
    private ITestOutputHelper _output;
    private int _level;

    public XUnitLogger(ITestOutputHelper output, int level)
    {
        _output = output;
        _level = level;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        var hasExcpetion = exception != null ? $", {exception}" : string.Empty;
        _output.WriteLine($"{GetLevel()}{logLevel}: {formatter(state, exception)}{hasExcpetion}");
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        _output.WriteLine($"======");

        Interlocked.Increment(ref _level);
        IScoped<object> scope = new object().CreateMockScope();
        scope.When(x => x.Dispose()).Do(_ =>
        {
            Interlocked.Decrement(ref _level);
            _output.WriteLine($"======");
        });
        return scope;
    }

    private string GetLevel()
    {
        return string.Join(string.Empty, Enumerable.Repeat("-", _level));
    }

    public void Dispose()
    {
    }

    public static XUnitLogger<T> Create(ITestOutputHelper output)
    {
        return new XUnitLogger<T>(output, 0);
    }
}
