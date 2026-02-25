using ProcessManager.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Windows;

namespace ProcessManager.Services
{
    public class ProcessService
    {
        public List<ProcessInfo> GetAllProcesses()
        {
            var processList = new List<ProcessInfo>();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    processList.Add(new ProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Priority = p.PriorityClass,
                        MemoryUsage = p.WorkingSet64,
                        ThreadCount = p.Threads.Count,
                        CpuTime = p.TotalProcessorTime,
                        AffinityMask = p.ProcessorAffinity,
                        ParentId = GetParentProcessId(p.Id)
                    });
                }
                catch
                {
                    // silently skip processes we cannot access
                }
            }

            return processList;
        }

        public bool SetProcessPriority(int processId, ProcessPriorityClass priority)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                try
                {
                    process.PriorityClass = priority;
                    return true;
                }
                finally
                {
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось изменить приоритет:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public bool SetProcessAffinity(int processId, IntPtr affinityMask)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                try
                {
                    process.ProcessorAffinity = affinityMask;
                    return true;
                }
                finally
                {
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось изменить привязку к ядрам:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public List<ThreadInfo> GetThreads(int processId)
        {
            var threads = new List<ThreadInfo>();

            try
            {
                Process process = Process.GetProcessById(processId);
                try
                {
                    foreach (ProcessThread t in process.Threads)
                    {
                        threads.Add(new ThreadInfo
                        {
                            Id = t.Id,
                            Priority = t.PriorityLevel,
                            State = t.ThreadState,
                            CpuTime = t.TotalProcessorTime
                        });
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
            catch { }

            return threads;
        }

        public void KillProcess(int processId)
        {
            try
            {
                Process process = Process.GetProcessById(processId);
                try
                {
                    process.Kill();
                }
                finally
                {
                    process.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось завершить процесс:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int GetParentProcessId(int pid)
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    string.Format("SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {0}", pid));
                try
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return Convert.ToInt32(obj["ParentProcessId"]);
                    }
                }
                finally
                {
                    searcher.Dispose();
                }
            }
            catch { }

            return -1;
        }

        public List<ProcessInfo> BuildProcessTree(List<ProcessInfo> processes)
        {
            Dictionary<int, ProcessInfo> dict = processes.ToDictionary(p => p.Id, p => p);
            List<ProcessInfo> roots = new List<ProcessInfo>();

            foreach (var p in processes)
            {
                if (p.ParentId.HasValue && dict.TryGetValue(p.ParentId.Value, out ProcessInfo parent))
                {
                    if (parent.Children == null)
                    {
                        parent.Children = new ProcessInfo[0];
                    }
                    parent.Children = parent.Children.Append(p).ToArray();
                }
                else
                {
                    roots.Add(p);
                }
            }

            return roots;
        }
    }
}