// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Store.Utils
{
    public static class BatchExtensions
    {
        public static Task StartTask(Action action, bool longRunning = true)
        {
            var task = new Task(action, longRunning ? TaskCreationOptions.LongRunning : TaskCreationOptions.None);
            task.Start(TaskScheduler.Default);
            return task;
        }

        internal static void ParallelForEach<T>(IEnumerable<T> objects, int threads, Action<int, T> action, CancelRequest cancel = null)
        {
            ExecuteInParallelBatches(objects, threads, 1, (thread, batch) => { action(thread, batch.Item2.First()); }, null, cancel);
        }

        public static void ExecuteInParallelBatches<T>(IEnumerable<T> objects, int threads, int batchSize, Action<int, Tuple<int, IList<T>>> action, int? queueCapacity = null, CancelRequest cancel = null)
        {
            if (cancel == null)
            {
                cancel = new CancelRequest();
            }

            using (var queue = queueCapacity == null ? new BlockingCollection<Tuple<int, IList<T>>>() : new BlockingCollection<Tuple<int, IList<T>>>(queueCapacity.Value))
            {
                Action<int> workerAction = (thread) =>
                {
                    while (!cancel.IsSet && !queue.IsCompleted)
                    {
                        try
                        {
                            if (queue.TryTake(out var batch, 1000))
                            {
                                if (!cancel.IsSet)
                                {
                                    action(thread, batch);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            cancel.Set();
                            queue.CompleteAdding(); // This is to let main thread to throw if it is blocked on write by limited queue capacity
                            throw;
                        }
                    }

                    if (cancel.IsSet)
                    {
                        queue.CompleteAdding(); // This is to let main thread to throw if it is blocked on write by limited queue capacity in case of external cancel
                    }
                };

                var workers = new List<Task> { StartTask(() => workerAction(0)) };

                try
                {
                    var batchId = 0;
                    var batchList = new List<T>();
                    foreach (var obj in objects)
                    {
                        if (cancel.IsSet)
                        {
                            break;
                        }

                        batchList.Add(obj);
                        if (batchList.Count == batchSize)
                        {
                            AddToQueueWithTrapping(queue, batchId, batchList, cancel, workers, threads, workerAction);
                            batchList = new List<T>();
                            batchId++;
                        }
                    }

                    if (!cancel.IsSet && batchList.Count > 0)
                    {
                        AddToQueueWithTrapping(queue, batchId, batchList, cancel, workers, threads, workerAction);
                    }

                    queue.CompleteAdding();

                    Task.WaitAll(workers.ToArray());
                }
                catch (Exception)
                {
                    queue.CompleteAdding();

                    cancel.Set();
                    throw;
                }
            }
        }

        private static void AddToQueueWithTrapping<T>(BlockingCollection<Tuple<int, IList<T>>> queue, int batchId, IList<T> batchList, CancelRequest cancel, ICollection<Task> workers, int maxWorkers, Action<int> workerAction)
        {
            try
            {
                queue.Add(new Tuple<int, IList<T>>(batchId, batchList));

                if (workers.Count < maxWorkers && queue.Count > 0)
                {
                    var id = workers.Count;
                    workers.Add(StartTask(() => workerAction(id)));
                }
            }
            catch (Exception e)
            {
                if (cancel.IsSet && IsCleanupQueueException(e))
                {
                    return;
                }

                throw;
            }
        }

        private static bool IsCleanupQueueException(Exception e)
        {
            return e.ToString().Contains("CompleteAdding may not be used concurrently with additions to the collection", StringComparison.OrdinalIgnoreCase)
                   || e.ToString().Contains("The collection has been marked as complete with regards to additions", StringComparison.OrdinalIgnoreCase)
                   || e is NullReferenceException
                   || e is ObjectDisposedException
                   || (e is ArgumentNullException && e.ToString().Contains("Value cannot be null", StringComparison.OrdinalIgnoreCase));
        }
    }
}
