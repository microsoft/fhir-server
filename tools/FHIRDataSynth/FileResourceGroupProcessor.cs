// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ResourceProcessorNamespace;

namespace FHIRDataSynth
{
    internal sealed class FileResourceGroupProcessor : ResourceGroupProcessor
    {
        private readonly string storePath;
        private readonly string resourceGroupDir;
        private readonly string blendPath;

        public FileResourceGroupProcessor(string storePath, string resourceGroupDir, string blendPath)
        {
            this.storePath = storePath;
            this.resourceGroupDir = resourceGroupDir;
            this.blendPath = blendPath;
        }

        protected override bool OnlyVerifyInput { get => blendPath == null; }

        public override string GetResourceGroupDir()
        {
            return resourceGroupDir;
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

        public override void LogInfo(string resourceGroupDir, string resourceName, string resourceId, string message)
        {
            Console.WriteLine($"INFO: {resourceGroupDir}{resourceName}/{resourceId}: {message}");
        }

        public override void LogWarning(string resourceGroupDir, string resourceName, string resourceId, string message)
        {
            Console.WriteLine($"WARNING: {resourceGroupDir}{resourceName}/{resourceId}: {message}");
        }
    }
}
