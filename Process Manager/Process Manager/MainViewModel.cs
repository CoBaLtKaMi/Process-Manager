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
using LiveCharts;
using LiveCharts.Wpf;

namespace ProcessManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly ProcessService _service = new ProcessService();
        private Timer _timer;
        private Timer _visualTimer;
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

        // Графики
        public SeriesCollection CpuSeries { get; private set; }
        public SeriesCollection MemoryPieSeries { get; private set; }
        private PerformanceCounter[] _cpuCounters;

        public ObservableCollection<ProcessInfo> Processes { get; } = new ObservableCollection<ProcessInfo>();
        public ObservableCollection<ProcessInfo> ProcessTree { get; } = new ObservableCollection<ProcessInfo>();

        public ProcessInfo SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                _selectedProcess = value;
                if (value != null) UpdateSelectedProcessDetails();
                OnPropertyChanged();
            }
        }

        public ProcessPriorityClass SelectedPriority
        {
            get => _selectedPriority;
            set
            {
                _selectedPriority = value;
                OnPropertyChanged();
            }
        }

        public bool[] SelectedCores
        {
            get => _selectedCores;
            set
            {
                _selectedCores = value;
                OnPropertyChanged();
            }
        }

        public string BinaryMask
        {
            get => _binaryMask;
            set
            {
                _binaryMask = value;
                OnPropertyChanged();
            }
        }

        public string HexMask
        {
            get => _hexMask;
            set
            {
                _hexMask = value;
                OnPropertyChanged();
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public bool ShowGuiOnly
        {
            get => _showGuiOnly;
            set
            {
                _showGuiOnly = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public bool ShowSystemOnly
        {
            get => _showSystemOnly;
            set
            {
                _showSystemOnly = value;
                FilterAndRefresh();
                OnPropertyChanged();
            }
        }

        public List<ThreadInfo> Threads
        {
            get => _threads;
            set
            {
                _threads = value;
                OnPropertyChanged();
            }
        }

        public IEnumerable<ProcessPriorityClass> AvailablePriorities { get; } = new[]
        {
            ProcessPriorityClass.Idle,
            ProcessPriorityClass.BelowNormal,
            ProcessPriorityClass.Normal,
            ProcessPriorityClass.AboveNormal,
            ProcessPriorityClass.High,
            ProcessPriorityClass.RealTime
        };

        public int UpdateIntervalSeconds
        {
            get => _updateIntervalSeconds;
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

            InitCharts();
            WarmUpCpuCounters();

            RestartTimer();
            LoadProcesses();
        }

        private void InitCharts()
        {
            CpuSeries = new SeriesCollection();
            MemoryPieSeries = new SeriesCollection();

            _cpuCounters = new PerformanceCounter[Environment.ProcessorCount];
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                var counter = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                _cpuCounters[i] = counter;

                CpuSeries.Add(new ColumnSeries
                {
                    Title = $"Ядро {i + 1}",
                    Values = new ChartValues<double> { 0 },
                    MaxColumnWidth = 50,
                    ColumnPadding = 5,
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb((byte)(80 + i * 40), (byte)(140 - i * 10), (byte)(220 - i * 20)))
                });
            }

            _visualTimer = new Timer(2000);
            _visualTimer.Elapsed += (s, e) => Application.Current?.Dispatcher.Invoke(UpdateCharts);
            _visualTimer.AutoReset = true;
            _visualTimer.Start();
        }

        private void WarmUpCpuCounters()
        {
            // Первый прогрев — два вызова на каждый счётчик
            for (int j = 0; j < 2; j++)
            {
                for (int i = 0; i < _cpuCounters.Length; i++)
                {
                    _cpuCounters[i].NextValue();
                }
                System.Threading.Thread.Sleep(500); // пауза 0.5 сек между вызовами
            }

            // Теперь уже можно обновлять график
            UpdateCpuChart();
        }

        private void UpdateCharts()
        {
            UpdateCpuChart();
            UpdateMemoryPieChart();
        }

        private void UpdateCpuChart()
        {
            for (int i = 0; i < _cpuCounters.Length; i++)
            {
                double value = _cpuCounters[i].NextValue();
                value = Math.Max(0, Math.Min(100, value)); // жёстко ограничиваем 0–100
                ((ColumnSeries)CpuSeries[i]).Values[0] = value;
            }
        }

        private void UpdateMemoryPieChart()
        {
            if (_allProcesses == null || _allProcesses.Count == 0) return;

            var top10 = _allProcesses
                .OrderByDescending(p => p.MemoryUsage)
                .Take(10)
                .ToList();

            // Обновляем существующие серии (без полной очистки)
            for (int i = 0; i < MemoryPieSeries.Count; i++)
            {
                if (i < top10.Count)
                {
                    var series = (PieSeries)MemoryPieSeries[i];
                    double mb = Math.Round(top10[i].MemoryUsage / 1024.0 / 1024.0, 1);
                    series.Values[0] = mb;
                    series.Title = $"{top10[i].Name} ({mb:N1} МБ)";
                }
                else
                {
                    // Удаляем лишние серии
                    MemoryPieSeries.RemoveAt(MemoryPieSeries.Count - 1);
                    i--;
                }
            }

            // Добавляем новые серии, если топ-10 вырос
            for (int i = MemoryPieSeries.Count; i < top10.Count; i++)
            {
                double mb = Math.Round(top10[i].MemoryUsage / 1024.0 / 1024.0, 1);
                MemoryPieSeries.Add(new PieSeries
                {
                    Title = $"{top10[i].Name} ({mb:N1} МБ)",
                    Values = new ChartValues<double> { mb },
                    DataLabels = true
                });
            }
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
                    if (pid == 0) return false;

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
            var sorted = descending
                ? Processes.OrderByDescending(selector).ToList()
                : Processes.OrderBy(selector).ToList();

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

            OnPropertyChanged(nameof(SelectedCores));
        }

        private void ChangeProcessPriority()
        {
            if (SelectedProcess == null) return;

            if (SelectedPriority == ProcessPriorityClass.RealTime)
            {
                var result = MessageBox.Show(
                    "Приоритет Realtime может нарушить работу системы. Продолжить?",
                    "Внимание!",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes) return;
            }

            if (_service.SetProcessPriority(SelectedProcess.Id, SelectedPriority))
            {
                SelectedProcess.Priority = SelectedPriority;
                OnPropertyChanged(nameof(SelectedProcess));
            }
        }

        private void ApplyAffinity()
        {
            if (SelectedProcess == null) return;

            var newMask = AffinityHelper.SetCoreMask(SelectedCores);

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

            var result = MessageBox.Show(
                $"Завершить процесс «{SelectedProcess.Name}» (PID: {SelectedProcess.Id})?",
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
            var roots = _service.BuildProcessTree(_allProcesses);
            ProcessTree.Clear();
            foreach (var root in roots)
            {
                ProcessTree.Add(root);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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

        public bool CanExecute(object parameter) => true;

        public void Execute(object parameter) => _execute(parameter);
    }
}