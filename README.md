# ⚙️ Process Manager

WPF-приложение для управления процессами Windows с возможностью изменения приоритетов, настройки CPU Affinity, просмотра потоков и визуализации нагрузки.

> Лабораторная работа №2 по дисциплине «Системное программное обеспечение»  
> ТвГТУ, кафедра информационных систем, 2026

---

## 📸 Возможности

| Функция | Описание |
|---|---|
| 📋 Список процессов | PID, имя, приоритет, память (МБ), CPU-время, число потоков |
| 🔍 Поиск и фильтрация | По имени, только с GUI, только системные |
| 🎯 Управление приоритетом | Idle → RealTime, с предупреждением для RealTime |
| 🧩 CPU Affinity | Чекбоксы ядер, отображение маски в Binary и Hex |
| 🧵 Потоки процесса | TID, приоритет, состояние, CPU-время |
| 🌲 Дерево процессов | Иерархия родитель–потомок в TreeView |
| 📊 Визуализация | График загрузки CPU по ядрам + Топ-10 процессов по памяти |
| ⏱️ Автообновление | Настраиваемый интервал: 1 / 3 / 5 / 10 / 30 / 60 сек |
| 🔴 Завершение процесса | Kill с диалогом подтверждения |

---

## 🛠️ Стек технологий

- **Язык:** C# 7.0+
- **Фреймворк:** .NET Framework 4.8
- **UI:** WPF (Windows Presentation Foundation)
- **Паттерн:** MVVM
- **Графики:** [LiveCharts](https://lvcharts.net/)
- **Системные вызовы:** `System.Diagnostics.Process`, `System.Management` (WMI)

---

## 🗂️ Структура проекта

```
Process Manager/
├── Models/
│   ├── ProcessInfo.cs          # Модель данных процесса
│   └── ThreadInfo.cs           # Модель данных потока
├── Services/
│   └── ProcessService.cs       # Получение и управление процессами
├── ViewModels/
│   └── MainViewModel.cs        # MVVM ViewModel, команды, таймеры
├── Utilities/
│   └── AffinityHelper.cs       # Работа с битовыми масками CPU Affinity
├── Converters/
│   └── MemoryToMBConverter.cs  # Конвертер байт → МБ для биндинга
├── MainWindow.xaml              # Разметка главного окна
├── MainWindow.xaml.cs           # Code-behind
└── App.xaml                     # Точка входа
```

---

## 🚀 Запуск

### Требования
- Windows 10 / 11
- [.NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)
- Visual Studio 2019 / 2022

### Установка

```bash
git clone https://github.com/your-username/process-manager.git
cd process-manager
```

Откройте `Process Manager.slnx` в Visual Studio, соберите решение и запустите.

> ⚠️ **Рекомендуется запускать от имени администратора** — без прав администратора часть системных процессов будет недоступна для чтения и изменения.

### Зависимости NuGet

```
LiveCharts.Wpf
System.Management
```

---

## 🔧 Ключевые детали реализации

### Получение списка процессов

```csharp
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
            ParentId = GetParentProcessId(p.Id)  // через WMI
        });
    }
    catch { /* системные процессы без доступа пропускаются */ }
}
```

### CPU Affinity через битовую маску

```csharp
// Проверить, включено ли ядро
bool enabled = (mask.ToInt64() & (1L << coreIndex)) != 0;

// Собрать маску из чекбоксов
long mask = 0;
for (int i = 0; i < cores.Length; i++)
    if (cores[i]) mask |= 1L << i;
process.ProcessorAffinity = new IntPtr(mask);
```

### Построение дерева процессов

```csharp
var dict = processes.ToDictionary(p => p.Id);
foreach (var p in processes)
{
    if (p.ParentId.HasValue && dict.TryGetValue(p.ParentId.Value, out var parent))
        parent.Children = parent.Children.Append(p).ToArray();
    else
        roots.Add(p);
}
```

---

## 🎨 Цветовое выделение приоритетов

| Приоритет | Цвет строки |
|---|---|
| RealTime | 🔴 Красный фон (`#FFCDD2`) |
| High | 🟠 Оранжевый фон (`#FFE0B2`) |
| AboveNormal | 🟡 Жёлтый фон (`#FFF9C4`) |
| Normal / ниже | ⚪ Стандартный |

---

## ⚠️ Обработка ошибок

Все операции с системными процессами оборачиваются в `try-catch`. Ошибка доступа (`Win32Exception`, код `5`) обрабатывается отдельно с понятным сообщением:

```csharp
catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
{
    MessageBox.Show("Недостаточно прав для изменения приоритета.");
}
```

Перед установкой приоритета **RealTime** всегда выводится диалог подтверждения, так как этот уровень может нарушить стабильность системы.

---


> Разработал: **Морозов А.Н.**, группа Б.ИВТ.ПРОМ.23.01  
> GitHub: [@CoBaLtKaMi](https://github.com/CoBaLtKaMi)
