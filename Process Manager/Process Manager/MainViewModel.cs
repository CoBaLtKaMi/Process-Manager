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
        private readonly ProcessService _service = new ProcessService();
        private Timer _timer;
        private List<ProcessInfo> _allProcesses = new List<ProcessInfo>();
        private ProcessInfo _selectedProcess;
        private List<ThreadInfo> _threads = new List<ThreadInfo>();
        private string _searchText = string.Empty;
        private bool _showGuiOnly;
        private bool _showSystemOnly;
        private ProcessPriorityClass _selectedPriority;
        private bool[] _selectedCores;
        private string _binaryMask = string.Empty;
        private string _hexMask = string.Empty;
        private int _updateIntervalSeconds = 5;

        public ObservableCollection<ProcessInfo> Processes { get; } = new ObservableCollection<ProcessInfo>();
        public ObservableCollection<ProcessInfo> ProcessTree { get; } = new ObservableCollection<ProcessInfo>();

        public ProcessInfo SelectedProcess
        {
            get { return _selectedProcess; }
            set
            {
                _selectedProcess = value;
                if (value != null) UpdateSelectedProcessDetails();
                OnPropertyChanged();
            }
        }

        public ProcessPriorityClass SelectedPriority
        {
            get { return _selectedPriority; }
            set
            {
                _selectedPriority = value;
                OnPropertyChanged();
            }
        }

        public bool[] SelectedCores
        {
            get { return _selectedCores; }
            set
            {
                _selectedCores = value;
                OnPropertyChanged();
            }
        }

        public string BinaryMask
        {
            get { return _binaryMask; }
            set
            {
                _binaryMask = value;
                OnPropertyChanged();
            }
        }

        public string HexMask
        {
            get { return _hexMask; }
            set
            {
                _hexMask = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                _searchText = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public bool ShowGuiOnly
        {
            get { return _showGuiOnly; }
            set
            {
                _showGuiOnly = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public bool ShowSystemOnly
        {
            get { return _showSystemOnly; }
            set
            {
                _showSystemOnly = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public List<ThreadInfo> Threads
        {
            get { return _threads; }
            set
            {
                _threads = value;
                OnPropertyChanged();
            }
        }

        public IEnumerable<ProcessPriorityClass> AvailablePriorities
        {
            get
            {
                return new ProcessPriorityClass[]
                {
                    ProcessPriorityClass.Idle,
                    ProcessPriorityClass.BelowNormal,
                    ProcessPriorityClass.Normal,
                    ProcessPriorityClass.AboveNormal,
                    ProcessPriorityClass.High,
                    ProcessPriorityClass.RealTime
                };
            }
        }

        public int UpdateIntervalSeconds
        {
            get { return _updateIntervalSeconds; }
            set
            {
                int newValue = value;
                if (newValue < 1) newValue = 1;
                if (newValue > 300) newValue = 300;

                _updateIntervalSeconds = newValue;
                OnPropertyChanged();
                RestartTimer();
            }
        }

        public ICommand RefreshCommand { get; private set; }
        public ICommand ChangePriorityCommand { get; private set; }
        public ICommand ChangeAffinityCommand { get; private set; }
        public ICommand KillCommand { get; private set; }
        public ICommand SortByNameCommand { get; private set; }
        public ICommand SortByCpuCommand { get; private set; }

        public MainViewModel()
        {
            _selectedCores = new bool[Environment.ProcessorCount];

            RefreshCommand = new RelayCommand(o => LoadProcesses());
            ChangePriorityCommand = new RelayCommand(o => ChangeProcessPriority());
            ChangeAffinityCommand = new RelayCommand(o => ApplyAffinity());
            KillCommand = new RelayCommand(o => KillSelected());
            SortByNameCommand = new RelayCommand(o => SortProcessesBy(p => p.Name));
            SortByCpuCommand = new RelayCommand(o => SortProcessesBy(p => p.CpuTime, true));

            RestartTimer();
            LoadProcesses();
        }

        private void RestartTimer()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            _timer = new Timer(UpdateIntervalSeconds * 1000);
            _timer.Elapsed += Timer_Elapsed;
            _timer.AutoReset = true;
            _timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (Application.Current != null)
            {
                Application.Current.Dispatcher.Invoke(LoadProcesses);
            }
        }

        private void LoadProcesses()
        {
            _allProcesses = _service.GetAllProcesses();
            FilterAndRefresh();
            UpdateTree();
        }

        private void FilterAndRefresh()
        {
            var query = _allProcesses.AsQueryable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                query = query.Where(p => p.Name.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (ShowGuiOnly)
            {
                query = query.Where(p => HasMainWindow(p.Id));
            }

            if (ShowSystemOnly)
            {
                query = query.Where(p => IsSystemProcess(p.Id));
            }

            Processes.Clear();
            foreach (var p in query)
            {
                Processes.Add(p);
            }
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
                    string name = p.ProcessName.ToLowerInvariant();
                    return name == "system" ||
                           name == "smss" ||
                           name == "csrss" ||
                           name == "wininit" ||
                           name == "services" ||
                           name == "lsass" ||
                           name == "svchost" ||
                           name.StartsWith("winlogon") ||
                           name == "explorer";
                }
            }
            catch
            {
                return false;
            }
        }

        private void SortProcessesBy<T>(Func<ProcessInfo, T> selector, bool descending = false)
        {
            List<ProcessInfo> sorted;
            if (descending)
            {
                sorted = Processes.OrderByDescending(selector).ToList();
            }
            else
            {
                sorted = Processes.OrderBy(selector).ToList();
            }

            Processes.Clear();
            foreach (var item in sorted)
            {
                Processes.Add(item);
            }
        }

        private void UpdateSelectedProcessDetails()
        {
            if (SelectedProcess == null) return;

            SelectedPriority = SelectedProcess.Priority;
            Threads = _service.GetThreads(SelectedProcess.Id);

            BinaryMask = AffinityHelper.ToBinaryString(SelectedProcess.AffinityMask);
            HexMask = AffinityHelper.ToHexString(SelectedProcess.AffinityMask);

            for (int i = 0; i < SelectedCores.Length; i++)
            {
                SelectedCores[i] = AffinityHelper.IsCoreEnabled(SelectedProcess.AffinityMask, i);
            }

            OnPropertyChanged("SelectedCores");
        }

        private void ChangeProcessPriority()
        {
            if (SelectedProcess == null) return;

            if (SelectedPriority == ProcessPriorityClass.RealTime)
            {
                MessageBoxResult result = MessageBox.Show(
                    "Приоритет Realtime может нарушить работу системы. Продолжить?",
                    "Внимание!",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }

            if (_service.SetProcessPriority(SelectedProcess.Id, SelectedPriority))
            {
                SelectedProcess.Priority = SelectedPriority;
                OnPropertyChanged("SelectedProcess");
            }
        }

        private void ApplyAffinity()
        {
            if (SelectedProcess == null) return;

            IntPtr newMask = AffinityHelper.SetCoreMask(SelectedCores);

            if (_service.SetProcessAffinity(SelectedProcess.Id, newMask))
            {
                SelectedProcess.AffinityMask = newMask;
                BinaryMask = AffinityHelper.ToBinaryString(newMask);
                HexMask = AffinityHelper.ToHexString(newMask);
            }
        }

        private void KillSelected()
        {
            if (SelectedProcess == null) return;

            MessageBoxResult result = MessageBox.Show(
                string.Format("Завершить процесс «{0}» (PID: {1})?", SelectedProcess.Name, SelectedProcess.Id),
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _service.KillProcess(SelectedProcess.Id);
                LoadProcesses();
            }
        }

        private void UpdateTree()
        {
            List<ProcessInfo> roots = _service.BuildProcessTree(_allProcesses);
            ProcessTree.Clear();
            foreach (ProcessInfo root in roots)
            {
                ProcessTree.Add(root);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName = null)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;

        public RelayCommand(Action<object> execute)
        {
            _execute = execute;
        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
}