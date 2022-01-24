using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ResourceProcessorNamespace
{
    internal class TargetProfile
    {
        public string name { get; set; }

        public int resourceGroupsCount { get; set; }

        public Dictionary<string, double> ratios { get; set; } = new Dictionary<string, double>();

        public void Validate()
        {
            if (resourceGroupsCount < 0)
            {
                throw new ApplicationException($"TargetProfil member 'resourceGroupsCount' contains invalid value {resourceGroupsCount}, must be 0 or greater.");
            }

            foreach (KeyValuePair<string, double> r in ratios)
            {
                if (r.Value < 0)
                {
                    throw new ApplicationException($"TargetProfile member 'ratios[{r.Key}]' contains invalid value {r.Value}, must 0 or greater.");
                }
            }
        }
    }

    internal abstract class ResourceProcessor
    {
        protected abstract void LogInfo(string message);

        protected abstract void LogError(string message);

        protected abstract Task<SortedSet<string>> GetResourceGroupDirsAsync();

        protected abstract ResourceGroupProcessor GetNewResourceGroupProcessor(string resourceGroupDir);

        private Task[] MakeResourceGroupTasks(
            Task[] currentResourceGroupTasks,
            int finishedResourceGroupTaskIndex,
            SortedSet<string> resourceGroupDirs,
            int resourceGroupTasksCount,
            Dictionary<Task, string> tasksGroupDir,
            TargetProfile targetProfile,
            int taskStartDelay)
        {
            Task[] newResourceGroupTasks;
            if (currentResourceGroupTasks != null)
            {
                newResourceGroupTasks = new Task[currentResourceGroupTasks.Length - 1 + Math.Min(resourceGroupDirs.Count, 1)];
                int j = 0;
                for (int i = 0; i < currentResourceGroupTasks.Length; i++)
                {
                    if (i != finishedResourceGroupTaskIndex)
                    {
                        newResourceGroupTasks[j++] = currentResourceGroupTasks[i];
                    }
                }

                if (j < newResourceGroupTasks.Length)
                {
                    string resourceGroupDir = resourceGroupDirs.First();
                    resourceGroupDirs.Remove(resourceGroupDir);
                    ResourceGroupProcessor rgp = GetNewResourceGroupProcessor(resourceGroupDir);
                    Task t = rgp.ProcessResourceGroupAsync(targetProfile);
                    tasksGroupDir.Add(t, resourceGroupDir);
                    newResourceGroupTasks[j] = t;
                }
            }
            else
            {
                newResourceGroupTasks = new Task[Math.Min(resourceGroupDirs.Count, resourceGroupTasksCount)];
                for (int i = 0; i < newResourceGroupTasks.Length; i++)
                {
                    string resourceGroupDir = resourceGroupDirs.First();
                    resourceGroupDirs.Remove(resourceGroupDir);
                    ResourceGroupProcessor rgp = GetNewResourceGroupProcessor(resourceGroupDir);
                    Task t = rgp.ProcessResourceGroupAsync(targetProfile);
                    tasksGroupDir.Add(t, resourceGroupDir);
                    newResourceGroupTasks[i] = t;
                    if (taskStartDelay > 0)
                    {
                        Task.Delay(taskStartDelay).Wait();
                    }
                }
            }

            return newResourceGroupTasks;
        }

        public void Process(int resourceGroupTasksCount, TargetProfile targetProfile, int taskStartDelay = 0)
        {
            try
            {
                if (resourceGroupTasksCount < 1)
                {
                    throw new ArgumentException("Must be larger than 0.", nameof(resourceGroupTasksCount));
                }

                targetProfile.Validate();
                if (taskStartDelay < 0)
                {
                    throw new ArgumentException("Must be equal or larger than 0.", nameof(taskStartDelay));
                }

                Task<SortedSet<string>> groupDirsTask = GetResourceGroupDirsAsync();
                groupDirsTask.Wait();
                SortedSet<string> allResourceGroupDirs = groupDirsTask.Result;
                if (targetProfile.resourceGroupsCount > allResourceGroupDirs.Count)
                {
                    throw new ArgumentOutOfRangeException($"Resource groups count ({targetProfile.resourceGroupsCount}) in target ratios file is greater than the actual number of resource groups ({allResourceGroupDirs.Count}).");
                }

                SortedSet<string> resourceGroupDirs = new SortedSet<string>(allResourceGroupDirs.Take(targetProfile.resourceGroupsCount));

                Dictionary<string, ResourcesResult> totals = new Dictionary<string, ResourcesResult>();
                Dictionary<Task, string> tasksGroupDir = new Dictionary<Task, string>();
                Task[] resourceGroupTasks = null;
                int finishedResourceGroupTaskIndex = 0;
                while (resourceGroupDirs.Count != 0 || (resourceGroupTasks != null && resourceGroupTasks.Length > 1))
                {
                    resourceGroupTasks = MakeResourceGroupTasks(resourceGroupTasks, finishedResourceGroupTaskIndex, resourceGroupDirs, resourceGroupTasksCount, tasksGroupDir, targetProfile, taskStartDelay);
                    finishedResourceGroupTaskIndex = Task.WaitAny(resourceGroupTasks);
                    Task<Dictionary<string, ResourcesResult>> task = (Task<Dictionary<string, ResourcesResult>>)resourceGroupTasks[finishedResourceGroupTaskIndex];
                    if (!task.IsCompletedSuccessfully)
                    {
                        LogError($"Resource group {tasksGroupDir[task]} task failed.{(task.Exception == null ? string.Empty : " " + task.Exception.Message)}");
                    }
                    else
                    {
                        SortedSet<string> keys = new SortedSet<string>(totals.Keys.Union(task.Result.Keys));
                        foreach (string key in keys)
                        {
                            if (!totals.ContainsKey(key))
                            {
                                totals.Add(key, task.Result[key]);
                            }
                            else if (task.Result.ContainsKey(key))
                            {
                                totals[key].Add(task.Result[key]);
                            }
                        }
                    }
                }

                double totalResourcesCount = totals.Sum(r => r.Value.OutputResourcesCount);
                LogInfo($"---------------------------------------------------------");
                LogInfo($"TOTALS {targetProfile.name}:");
                foreach (KeyValuePair<string, ResourcesResult> r in totals)
                {
                    LogInfo($"{r.Key}: {r.Value.InputSelectedResorcesCount}, {r.Value.OutputResourcesCount}, {String.Format("{0:0.000000}", r.Value.OutputResourcesCount / totalResourcesCount * 100)}%");
                }
            }
            catch (Exception ex)
            {
                LogError($"Exception: {ex.Message}");
            }
        }
    }
}
