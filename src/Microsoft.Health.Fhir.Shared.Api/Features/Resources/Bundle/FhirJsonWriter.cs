// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle;

/// <summary>
/// A low level writer for FHIR JSON.
/// </summary>
public class FhirJsonWriter : IDisposable, IAsyncDisposable
{
    private readonly JsonWriterOptions _writerOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonWriterOptions _indentedWriterOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true,
    };

    private readonly Utf8JsonWriter _writer;

    private FhirJsonWriter(Stream outputStream, bool pretty = false)
    {
        _writer = new Utf8JsonWriter(outputStream, pretty ? _indentedWriterOptions : _writerOptions);
    }

    public static FhirJsonWriter Create(Stream outputStream, bool pretty = false)
    {
        return new FhirJsonWriter(outputStream, pretty);
    }

    public FhirJsonWriter WriteStartObject()
    {
        _writer.WriteStartObject();
        return this;
    }

    public FhirJsonWriter WriteStartObject(string propertyName)
    {
        EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
        _writer.WriteStartObject(propertyName);
        return this;
    }

    public FhirJsonWriter WriteEndObject()
    {
        _writer.WriteEndObject();
        return this;
    }

    public FhirJsonWriter WriteObject(string propertyName, Action<FhirJsonWriter> action)
    {
        EnsureArg.IsNotNullOrEmpty(propertyName, nameof(propertyName));
        _writer.WriteStartObject(propertyName);
        action(this);
        _writer.WriteEndObject();
        return this;
    }

    public FhirJsonWriter WriteOptionalString(string name, string value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        if (!string.IsNullOrEmpty(value))
        {
            _writer.WriteString(name, value);
        }

        return this;
    }

    public FhirJsonWriter WriteString(string name, string value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));
        EnsureArg.IsNotNullOrEmpty(value, nameof(value));

        return WriteOptionalString(name, value);
    }

    public FhirJsonWriter WriteNumber(string name, int value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        _writer.WriteNumber(name, value);

        return this;
    }

    public FhirJsonWriter WriteOptionalNumber(string name, int? value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        if (value.HasValue)
        {
            _writer.WriteNumber(name, value.Value);
        }

        return this;
    }

    public FhirJsonWriter WriteRawProperty(string name, ReadOnlySpan<byte> value)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));

        _writer.WritePropertyName(name);
        _writer.WriteRawValue(value, true);

        return this;
    }

    public FhirJsonWriter WriteArray<T>(string name, IEnumerable<T> values, Action<FhirJsonWriter, T> itemWriter)
    {
        EnsureArg.IsNotNullOrEmpty(name, nameof(name));
        EnsureArg.IsNotNull(values, nameof(values));
        EnsureArg.IsNotNull(itemWriter, nameof(itemWriter));

        _writer.WriteStartArray(name);

        foreach (T item in values)
        {
            _writer.WriteStartObject();
            itemWriter(this, item);
            _writer.WriteEndObject();
        }

        _writer.WriteEndArray();

        return this;
    }

    /// <summary>
    /// Conditionally writes the provided action if the predicate is true.
    /// </summary>
    /// <param name="predicate">Expression to test.</param>
    /// <param name="action">Action to run.</param>
    /// <returns><see cref="FhirJsonWriter" /></returns>
    public FhirJsonWriter Condition(bool predicate, Action<FhirJsonWriter> action)
    {
        if (predicate)
        {
            action(this);
        }

        return this;
    }

    public BundleIfElse ConditionIf(bool predicate, Action<FhirJsonWriter> action)
    {
        if (predicate)
        {
            action(this);
        }

        return new BundleIfElse(this, predicate);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _writer?.Dispose();
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_writer != null)
        {
            await _writer.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}
