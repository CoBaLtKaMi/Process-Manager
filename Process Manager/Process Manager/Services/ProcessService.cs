using ProcessManager.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
                catch { }
            }
            return processList;
        }

        public bool SetProcessPriority(int processId, ProcessPriorityClass priority)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    process.PriorityClass = priority;
                    return true;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                MessageBox.Show("Недостаточно прав для изменения приоритета.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public bool SetProcessAffinity(int processId, IntPtr affinityMask)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    process.ProcessorAffinity = affinityMask;
                    return true;
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                MessageBox.Show("Недостаточно прав для изменения привязки.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public List<ThreadInfo> GetThreads(int processId)
        {
            var threads = new List<ThreadInfo>();
            try
            {
                using (Process process = Process.GetProcessById(processId))
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
            }
            catch { }
            return threads;
        }

        public void KillProcess(int processId)
        {
            try
            {
                using (Process process = Process.GetProcessById(processId))
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static int GetParentProcessId(int pid)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {pid}"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return Convert.ToInt32(obj["ParentProcessId"]);
                    }
                }
            }
            catch { }
            return -1;
        }

        public List<ProcessInfo> BuildProcessTree(List<ProcessInfo> processes)
        {
            var dict = processes.ToDictionary(p => p.Id, p => p);
            var roots = new List<ProcessInfo>();

            foreach (var p in processes)
            {
                if (p.ParentId.HasValue && dict.TryGetValue(p.ParentId.Value, out var parent))
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