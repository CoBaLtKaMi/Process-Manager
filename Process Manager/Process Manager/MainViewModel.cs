using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using ProcessManager.Models;
using ProcessManager.Services;
using ProcessManager.Utilities;

namespace ProcessManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ProcessService service;
        private readonly Timer timer;
        private List<ProcessInfo> allProcesses;
        private ProcessInfo selectedProcess;
        private List<ThreadInfo> threads;
        private string searchText;
        private bool showGuiOnly;
        private bool showSystemOnly;
        private ProcessPriorityClass selectedPriority;
        private bool[] selectedCores;
        private string binaryMask;
        private string hexMask;

        public ObservableCollection<ProcessInfo> Processes { get; private set; }
        public ObservableCollection<ProcessInfo> ProcessTree { get; private set; }

        public ProcessInfo SelectedProcess
        {
            get => selectedProcess;
            set
            {
                selectedProcess = value;
                if (value != null) UpdateSelectedProcessDetails();
                OnPropertyChanged();   // ← без параметра, CallerMemberName сам подставит имя
            }
        }

        public ProcessPriorityClass SelectedPriority
        {
            get => selectedPriority;
            set
            {
                selectedPriority = value;
                OnPropertyChanged();
            }
        }

        public bool[] SelectedCores
        {
            get => selectedCores;
            set
            {
                selectedCores = value;
                OnPropertyChanged();
            }
        }

        public string BinaryMask
        {
            get => binaryMask;
            set
            {
                binaryMask = value;
                OnPropertyChanged();
            }
        }

        public string HexMask
        {
            get => hexMask;
            set
            {
                hexMask = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => searchText;
            set
            {
                searchText = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public bool ShowGuiOnly
        {
            get => showGuiOnly;
            set
            {
                showGuiOnly = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public bool ShowSystemOnly
        {
            get => showSystemOnly;
            set
            {
                showSystemOnly = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public List<ThreadInfo> Threads
        {
            get => threads;
            set
            {
                threads = value;
                OnPropertyChanged();
            }
        }

        public IEnumerable<ProcessPriorityClass> AvailablePriorities =>
            Enum.GetValues(typeof(ProcessPriorityClass)).Cast<ProcessPriorityClass>();

        public ICommand RefreshCommand { get; private set; }
        public ICommand ChangePriorityCommand { get; private set; }
        public ICommand ChangeAffinityCommand { get; private set; }
        public ICommand KillCommand { get; private set; }
        public ICommand SortByNameCommand { get; private set; }
        public ICommand SortByCpuCommand { get; private set; }

        public MainViewModel()
        {
            service = new ProcessService();
            allProcesses = new List<ProcessInfo>();
            threads = new List<ThreadInfo>();
            searchText = string.Empty;
            binaryMask = string.Empty;
            hexMask = string.Empty;
            selectedCores = new bool[Environment.ProcessorCount];
            Processes = new ObservableCollection<ProcessInfo>();
            ProcessTree = new ObservableCollection<ProcessInfo>();

            RefreshCommand = new RelayCommand(obj => LoadProcesses());
            ChangePriorityCommand = new RelayCommand(obj => ChangeProcessPriority());
            ChangeAffinityCommand = new RelayCommand(obj => ApplyAffinity());
            KillCommand = new RelayCommand(obj => KillSelected());
            SortByNameCommand = new RelayCommand(obj => SortProcessesBy(p => p.Name));
            SortByCpuCommand = new RelayCommand(obj => SortProcessesBy(p => p.CpuTime, true));

            timer = new Timer(5000);
            timer.Elapsed += (s, e) => Application.Current?.Dispatcher.Invoke(LoadProcesses);
            timer.Start();

            LoadProcesses();
        }

        private void LoadProcesses()
        {
            allProcesses = service.GetAllProcesses();
            FilterAndRefresh();
            UpdateTree();
        }

        private void FilterAndRefresh()
        {
            var query = allProcesses.AsQueryable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(p => p.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (ShowGuiOnly)
                query = query.Where(p => HasMainWindow(p.Id));

            if (ShowSystemOnly)
                query = query.Where(p => IsSystemProcess(p.Id));

            Processes.Clear();
            foreach (var p in query)
                Processes.Add(p);
        }

        private static bool HasMainWindow(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    return p.MainWindowHandle != IntPtr.Zero;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSystemProcess(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    return p.SessionId == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void SortProcessesBy<T>(Func<ProcessInfo, T> selector, bool descending = false)
        {
            var sorted = descending
                ? Processes.OrderByDescending(selector).ToList()
                : Processes.OrderBy(selector).ToList();

            Processes.Clear();
            foreach (var item in sorted)
                Processes.Add(item);
        }

        private void UpdateSelectedProcessDetails()
        {
            if (SelectedProcess == null) return;

            SelectedPriority = SelectedProcess.Priority;
            Threads = service.GetThreads(SelectedProcess.Id);

            BinaryMask = AffinityHelper.ToBinaryString(SelectedProcess.AffinityMask);
            HexMask = AffinityHelper.ToHexString(SelectedProcess.AffinityMask);

            for (int i = 0; i < selectedCores.Length; i++)
                selectedCores[i] = AffinityHelper.IsCoreEnabled(SelectedProcess.AffinityMask, i);

            OnPropertyChanged(nameof(SelectedCores));
        }

        private void ChangeProcessPriority()
        {
            if (SelectedProcess == null) return;

            if (SelectedPriority == ProcessPriorityClass.RealTime)
            {
                var result = MessageBox.Show(
                    "Установка приоритета RealTime может привести к нестабильности системы.\nПродолжить?",
                    "Предупреждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            if (service.SetProcessPriority(SelectedProcess.Id, SelectedPriority))
            {
                SelectedProcess.Priority = SelectedPriority;
                OnPropertyChanged(nameof(SelectedProcess));
            }
        }

        private void ApplyAffinity()
        {
            if (SelectedProcess == null) return;

            var newMask = AffinityHelper.SetCoreMask(selectedCores);

            if (service.SetProcessAffinity(SelectedProcess.Id, newMask))
            {
                SelectedProcess.AffinityMask = newMask;
                BinaryMask = AffinityHelper.ToBinaryString(newMask);
                HexMask = AffinityHelper.ToHexString(newMask);
            }
        }

        private void KillSelected()
        {
            if (SelectedProcess == null) return;

            var result = MessageBox.Show(
                $"Завершить процесс «{SelectedProcess.Name}» (PID: {SelectedProcess.Id})?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                service.KillProcess(SelectedProcess.Id);
                LoadProcesses();
            }
        }

        private void UpdateTree()
        {
            var roots = service.BuildProcessTree(allProcesses);

            ProcessTree.Clear();
            foreach (var root in roots)
                ProcessTree.Add(root);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> execute;

        public RelayCommand(Action<object> execute)
        {
            this.execute = execute;
        }

#pragma warning disable CS0067
        public event EventHandler CanExecuteChanged;
#pragma warning restore CS0067

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => execute(parameter);
    }
}