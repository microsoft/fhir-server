// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;

namespace Microsoft.Health.Internal.Fhir.IncludePerf.DataGenerator
{
    /// <summary>
    /// Writes NDJSON lines for a single resource type, rolling over to a new shard file once
    /// <see cref="DatasetProfile.MaxLinesPerFile"/> lines have been written. Each worker owns its own
    /// instance so no locking is required on the hot path.
    /// </summary>
    internal sealed class ShardedNdjsonWriter : IDisposable
    {
        private const int BufferSize = 1 << 20;

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly string _outputDirectory;
        private readonly string _resourceType;
        private readonly int _workerId;
        private readonly int _maxLinesPerFile;
        private readonly List<string> _files = new();

        private StreamWriter _writer;
        private int _linesInCurrentFile;
        private int _shardIndex;

        internal ShardedNdjsonWriter(string outputDirectory, string resourceType, int workerId, int maxLinesPerFile)
        {
            _outputDirectory = outputDirectory;
            _resourceType = resourceType;
            _workerId = workerId;
            _maxLinesPerFile = maxLinesPerFile;
        }

        internal long LineCount { get; private set; }

        internal IReadOnlyList<string> Files => _files;

        internal string ResourceType => _resourceType;

        internal void WriteLine(string json)
        {
            if (_writer == null || _linesInCurrentFile >= _maxLinesPerFile)
            {
                RollOver();
            }

            _writer.Write(json);
            _writer.Write('\n');
            _linesInCurrentFile++;
            LineCount++;
        }

        public void Dispose()
        {
            _writer?.Flush();
            _writer?.Dispose();
            _writer = null;
        }

        private void RollOver()
        {
            _writer?.Flush();
            _writer?.Dispose();

            string path = Path.Combine(
                _outputDirectory,
                $"{_resourceType}-w{_workerId:D2}-{_shardIndex:D3}.ndjson");

            _writer = new StreamWriter(
                new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan),
                Utf8NoBom,
                BufferSize);

            _files.Add(path);
            _shardIndex++;
            _linesInCurrentFile = 0;
        }
    }
}
