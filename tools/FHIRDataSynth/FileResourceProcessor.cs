// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ResourceProcessorNamespace;

namespace FHIRDataSynth
{
    internal sealed class FileResourceProcessor : ResourceProcessor
    {
        private const string OutputBlobContainerNamePrefix = "blend-";
        private readonly string storePath;
        private readonly string blendPath;

        public FileResourceProcessor(string storePath, string blendPath)
        {
            if (blendPath == null)
            {
                // Blend path is null, we will verify that files in storePath are correct.
                string verifyBlendParent = Path.GetDirectoryName(storePath);
                string verifyBlendDir = Path.GetFileName(storePath);

                // verifyBlendParent.EndsWith check is for case we are writing into root dir. Then verifyBlendParent ends with Path.DirectorySeparatorChar, no need to add one.
                this.storePath = verifyBlendParent + (verifyBlendParent.EndsWith(Path.DirectorySeparatorChar) ? string.Empty : Path.DirectorySeparatorChar) + OutputBlobContainerNamePrefix + verifyBlendDir + Path.DirectorySeparatorChar;
            }
            else
            {
                if (!storePath.EndsWith(Path.DirectorySeparatorChar))
                {
                    storePath += Path.DirectorySeparatorChar;
                }

                this.storePath = storePath;

                string blendParent = Path.GetDirectoryName(blendPath);
                string blendDir = Path.GetFileName(blendPath);
                this.blendPath = blendParent + Path.DirectorySeparatorChar + OutputBlobContainerNamePrefix + blendDir + Path.DirectorySeparatorChar;
            }
        }

        protected override void LogInfo(string message)
        {
            Console.WriteLine($"INFO: {message}");
        }

        protected override void LogError(string message)
        {
            Console.WriteLine($"ERROR: {message}");
        }

        protected override Task<SortedSet<string>> GetResourceGroupDirsAsync()
        {
            string[] dirs = Directory.GetDirectories(storePath);
            SortedSet<string> ret = new SortedSet<string>();
            foreach (string dirPath in dirs)
            {
                string dir = Path.GetRelativePath(storePath, dirPath) + "/";
                ret.Add(dir);
            }

            return Task.FromResult(ret);
        }

        protected override ResourceGroupProcessor GetNewResourceGroupProcessor(string resourceGroupDir)
        {
            return new FileResourceGroupProcessor(storePath, resourceGroupDir, blendPath);
        }
    }
}
