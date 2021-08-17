// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.IdentityModel.Tokens;
using Polly;

namespace ImportTool
{
    public class SingleFileSpliter
    {
        private Uri _sourceUri;
        private ICloudBlob _sourceBlob;

        public SingleFileSpliter(
            ICloudBlob sourceBlob,
            Uri sourceUri,
            long splitSizeInBytes,
            long blockSizeInBytes,
            int maxSpliterCountPerFile,
            int maxUploaderCountPerSplitedFile)
        {
            _sourceBlob = sourceBlob;
            _sourceUri = sourceUri;
            SplitSizeInBytes = splitSizeInBytes;
            MaxSpliterCountPerFile = maxSpliterCountPerFile;
            MaxUploaderCountPerSplitedFile = maxUploaderCountPerSplitedFile;
            BlockSizeInBytes = blockSizeInBytes;
        }

        public long SplitSizeInBytes { get; set; }

        public int MaxSpliterCountPerFile { get; set; }

        public int MaxUploaderCountPerSplitedFile { get; set; }

        public long BlockSizeInBytes { get; set; }

        public async Task Split()
        {
            var points = GetSplitPoints();

            List<Task> runningTasks = new List<Task>();

            foreach (var (blockMetas, targetCloudBlockBlob) in points)
            {
                if (runningTasks.Count >= MaxSpliterCountPerFile)
                {
                    var task = await Task.WhenAny(runningTasks.ToArray());

                    await task;
                    runningTasks.Remove(task);
                }

                runningTasks.Add(UploadBlob(blockMetas, targetCloudBlockBlob));
            }

            await Task.WhenAll(runningTasks);
        }

        private IEnumerable<Tuple<IEnumerable<BlockMeta>, CloudBlockBlob>> GetSplitPoints()
        {
            using (var stream = _sourceBlob.OpenRead())
            {
                long totalLength = _sourceBlob.Properties.Length;
                IList<Tuple<long, long>> points = new List<Tuple<long, long>>();
                long offset = 0;
                long length = 0;
                int index = 0;

                while (true)
                {
                    CloudBlockBlob cloudBlockBlob = _sourceBlob.Container.GetBlockBlobReference($"splited/{_sourceBlob.Name}_{SplitSizeInBytes}_{index}");
                    IEnumerable<BlockMeta> blockMetas;

                    length = SplitSizeInBytes;

                    if (offset + length >= totalLength)
                    {
                        blockMetas = SplitFileToBlocks(offset, totalLength - offset);
                        yield return new Tuple<IEnumerable<BlockMeta>, CloudBlockBlob>(blockMetas, cloudBlockBlob);
                        break;
                    }

                    stream.Seek(offset + length - 1, SeekOrigin.Begin);

                    // Reads a line. A line is defined as a sequence of characters followed by
                    // a carriage return ('\r'), a line feed ('\n'), or a carriage return
                    // immediately followed by a line feed.
                    int count = 0;
                    while (true)
                    {
                        int c = stream.ReadByte();

                        // read to end, EOF shouldn't be contained
                        if (c < 0)
                        {
                            count--;
                        }

                        // \n - UNIX   \r\n - DOS   \r - Mac, -1 is END
                        if (c == '\n' || c == '\r')
                        {
                            if (c == '\r' && stream.ReadByte() == '\n')
                            {
                                count++;
                            }

                            break;
                        }

                        count++;
                    }

                    length += count;
                    blockMetas = SplitFileToBlocks(offset, length);
                    offset += length;
                    index++;

                    yield return new Tuple<IEnumerable<BlockMeta>, CloudBlockBlob>(blockMetas, cloudBlockBlob);
                }
            }
        }

        private IEnumerable<BlockMeta> SplitFileToBlocks(long offset, long length)
        {
            List<BlockMeta> blockMetas = new List<BlockMeta>();
            while (length > BlockSizeInBytes)
            {
                blockMetas.Add(new BlockMeta
                {
                    Id = Base64UrlEncoder.Encode(Guid.NewGuid().ToString()),
                    Offset = offset,
                    Length = BlockSizeInBytes,
                });

                length -= BlockSizeInBytes;
                offset += BlockSizeInBytes;
            }

            blockMetas.Add(new BlockMeta
            {
                Id = Base64UrlEncoder.Encode(Guid.NewGuid().ToString()),
                Offset = offset,
                Length = length,
            });

            return blockMetas;
        }

        private async Task UploadBlob(IEnumerable<BlockMeta> blockMetas, CloudBlockBlob cloudBlockBlob)
        {
            List<Task> runningTasks = new List<Task>();

            foreach (var blockMeta in blockMetas)
            {
                if (runningTasks.Count >= MaxUploaderCountPerSplitedFile)
                {
                    Task task = await Task.WhenAny(runningTasks.ToArray());

                    await task;
                    runningTasks.Remove(task);
                }

                Task putBlockTask = Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(() => cloudBlockBlob.PutBlockAsync(blockMeta.Id, _sourceUri, blockMeta.Offset, blockMeta.Length, null));

                runningTasks.Add(putBlockTask);
            }

            await Task.WhenAll(runningTasks);

            IEnumerable<string> blockIds = blockMetas.Select(blockMeta => blockMeta.Id);
            await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(() => cloudBlockBlob.PutBlockListAsync(blockIds));
        }

        private class BlockMeta
        {
            public string Id { get; set; }

            public long Offset { get; set; }

            public long Length { get; set; }

            public CloudBlockBlob CloudBlockBlob { get; set; }
        }
    }
}
