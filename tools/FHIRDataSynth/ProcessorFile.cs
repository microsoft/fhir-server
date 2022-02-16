// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ResourceProcessorNamespace;

namespace FHIRDataSynth
{
    internal class FileResourceGroupProcessor : ResourceGroupProcessor
    {
        private readonly string storePath;
        private readonly string resourceGroupDir;
        private readonly string blendPath;

        public override string GetResourceGroupDir()
        {
            return resourceGroupDir;
        }

        public FileResourceGroupProcessor(string storePath, string resourceGroupDir, string blendPath)
        {
            this.storePath = storePath;
            this.resourceGroupDir = resourceGroupDir;
            this.blendPath = blendPath;
        }

        protected override Task MakeOutputResourceGroupDirAsync()
        {
            if (blendPath != null)
            {
                string resourceGroupPath = blendPath + resourceGroupDir;
                if (Directory.Exists(resourceGroupPath))
                {
                    // Safety check, so we don't start overwriting if we already downloaded.
                    throw new FHIRDataSynthException($"Directory '{resourceGroupPath}' already exists!");
                }

                Directory.CreateDirectory(resourceGroupPath);
            }

            return Task.CompletedTask;
        }

        /*protected async override Task<ResourcesReturn> ProcessResourcesStreamAsync<T>(string resourceName, HashSet<string> patients, double dbSyntheaRatio)
        {
            using (FileStream fsw = new FileStream(blendPath + resourceGroupDir + resourceName + RDUtility.ResourcesExtension, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024 * 64, FileOptions.SequentialScan | FileOptions.Asynchronous))
            using (StreamWriter streamWriter = new StreamWriter(fsw, Encoding.UTF8, 1024 * 1024 * 64))
            using (FileStream fsr = new FileStream(storePath + resourceGroupDir + resourceName + RDUtility.ResourcesExtension, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024 * 64, FileOptions.SequentialScan | FileOptions.Asynchronous))
            //using (FileStream fsr = File.OpenRead(storePath + resourceGroupDir + resourceName + RDUtility.ResourcesExtension))
            using (StreamReader streamReader = new StreamReader(fsr, Encoding.UTF8, false, 1024 * 1024 * 64))
            {
                return await ProcessResourcesAsync<T>(resourceGroupDir, resourceName, streamReader, streamWriter, patients, dbSyntheaRatio);
            }
        }*/
        protected async override Task<StreamReader> GetStreamReader(string resourceName)
        {
            FileStream fsr = new FileStream(storePath + resourceGroupDir + resourceName + RDUtility.ResourcesExtension, FileMode.Open, FileAccess.Read, FileShare.None, 1024 * 1024 * 64, FileOptions.SequentialScan /*| FileOptions.Asynchronous*/);
            return await Task.FromResult(new StreamReader(fsr, Encoding.UTF8, false, 1024 * 1024 * 64));
        }

        protected override Task<StreamWriter> GetStreamWriter(string resourceName)
        {
            FileStream fileStream = new FileStream(blendPath + resourceGroupDir + resourceName + RDUtility.ResourcesExtension, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024 * 64, FileOptions.SequentialScan /*| FileOptions.Asynchronous*/);
            return Task.FromResult(new StreamWriter(fileStream, new UTF8Encoding(false), 2 * 1024 * 1024));
        }

        protected override bool OnlyVerifyInput { get => blendPath == null; }

        public override void LogInfo(string resourceGroupDir, string resourceName, string resourceId, string message)
        {
            Console.WriteLine($"INFO: {resourceGroupDir}{resourceName}/{resourceId}: {message}");
        }

        public override void LogWarning(string resourceGroupDir, string resourceName, string resourceId, string message)
        {
            Console.WriteLine($"WARNING: {resourceGroupDir}{resourceName}/{resourceId}: {message}");
        }
    }

    internal class RDResourceProcessor : ResourceProcessor
    {
        private const string OutputBlobContainerNamePrefix = "blend-";
        private readonly string storePath;
        private readonly string blendPath;

        public RDResourceProcessor(string storePath, string blendPath)
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
