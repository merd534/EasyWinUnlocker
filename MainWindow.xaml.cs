using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace SystemAnalyzer
{
    public partial class MainWindow : Window
    {
        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll")]
        private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, ref bool isDebuggerPresent);

        [DllImport("kernel32.dll")]
        private static extern void OutputDebugString(string lpOutputString);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPathW(string pszPath);

        [DllImport("shell32.dll")]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;

        private string currentExplorerPath = "";
        private ModuleManager _moduleManager = new ModuleManager();

        public MainWindow()
        {
            InitializeComponent();

            // Инициализируем ItemsSource пустым списком
            StartupGrid.ItemsSource = new List<StartupItem>();
            ProcessesGrid.ItemsSource = new List<ProcessInfo>();
            ExplorerGrid.ItemsSource = new List<FileSystemItem>();
            WinlogonGrid.ItemsSource = new List<WinlogonItem>();
            CMDLineGrid.ItemsSource = new List<CMDCommand>();

            LoadProcesses();
            LoadAllStartupItems();
            InitializeExplorer();

            // Загружаем модули
            _moduleManager.LoadAllModules();
            RefreshModulesList();
        }

        #region Модульная система
        private void RefreshModulesList()
        {
            ModulesCombo.Items.Clear();
            foreach (var module in _moduleManager.LoadedModules)
            {
                ModulesCombo.Items.Add(module.ModuleName);
            }

            if (ModulesCombo.Items.Count > 0)
            {
                ModulesCombo.SelectedIndex = 0;
            }

            ModuleStatus.Text = $"Загружено модулей: {_moduleManager.LoadedModules.Count}";
        }

        private void ModulesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModulesCombo.SelectedItem != null)
            {
                string moduleName = ModulesCombo.SelectedItem.ToString();
                var module = _moduleManager.GetModuleByName(moduleName);
                if (module != null)
                {
                    ModuleContent.Content = module.ModuleInterface;
                    ModuleStatus.Text = $"Модуль: {module.ModuleName} - {module.ModuleDescription}";
                }
            }
        }

        private void LoadModule_Click(object sender, RoutedEventArgs e)
        {
            // Перезагрузка всех модулей
            _moduleManager.UnloadAllModules();
            _moduleManager.LoadAllModules();
            RefreshModulesList();
            MessageBox.Show("Модули перезагружены");
        }

        private void UnloadModule_Click(object sender, RoutedEventArgs e)
        {
            if (ModulesCombo.SelectedItem != null)
            {
                string moduleName = ModulesCombo.SelectedItem.ToString();
                // В текущей реализации можно только выгрузить все модули
                _moduleManager.UnloadAllModules();
                RefreshModulesList();
                MessageBox.Show($"Модули выгружены");
            }
        }
        #endregion

        #region Обновленная автозагрузка
        public class StartupItem
        {
            public string Name { get; set; }
            public string Path { get; set; }
            public string Location { get; set; }
            public bool Enabled { get; set; }
            public string Type { get; set; }
        }

        private void LoadAllStartupItems()
        {
            var startupItems = new List<StartupItem>();

            // Реестр HKCU
            LoadRegistryStartup(startupItems, Registry.CurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run", "Реестр");

            // Реестр HKLM
            LoadRegistryStartup(startupItems, Registry.LocalMachine,
                @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run", "Реестр");

            // Папка автозагрузки
            LoadFolderStartup(startupItems);

            // Службы
            LoadServices(startupItems);

            // Убедимся, что StartupGrid инициализирован
            if (StartupGrid != null)
            {
                StartupGrid.ItemsSource = startupItems;
            }

            StartupStatusText.Text = $"Всего элементов: {startupItems.Count}";
        }

        private void LoadRegistryStartup(List<StartupItem> items, RegistryKey root,
            string subKey, string location, string type)
        {
            try
            {
                using (var key = root.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            var value = key.GetValue(valueName)?.ToString();
                            if (!string.IsNullOrEmpty(value))
                            {
                                items.Add(new StartupItem
                                {
                                    Name = valueName,
                                    Path = value,
                                    Location = location,
                                    Enabled = true,
                                    Type = type
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void LoadFolderStartup(List<StartupItem> items)
        {
            try
            {
                var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                foreach (var file in Directory.GetFiles(startupFolder))
                {
                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Location = "Startup Folder",
                        Enabled = true,
                        Type = "Файл"
                    });
                }

                // Common Startup
                var commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                foreach (var file in Directory.GetFiles(commonStartup))
                {
                    items.Add(new StartupItem
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Location = "Common Startup",
                        Enabled = true,
                        Type = "Файл"
                    });
                }
            }
            catch { }
        }

        private void LoadServices(List<StartupItem> items)
        {
            try
            {
                var services = ServiceController.GetServices();
                foreach (var service in services)
                {
                    if (service.StartType == ServiceStartMode.Automatic)
                    {
                        items.Add(new StartupItem
                        {
                            Name = service.ServiceName,
                            Path = service.DisplayName,
                            Location = "Службы",
                            Enabled = service.Status == ServiceControllerStatus.Running,
                            Type = "Служба"
                        });
                    }
                }
            }
            catch { }
        }

        private void RefreshAllStartup_Click(object sender, RoutedEventArgs e)
        {
            LoadAllStartupItems();
        }

        private void StartupCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Проверяем, что данные уже загружены
            if (StartupGrid?.ItemsSource != null)
            {
                FilterStartupItems();
            }
        }

        private void FilterStartupItems()
        {
            // Проверяем, что ItemsSource инициализирован
            if (StartupGrid?.ItemsSource == null)
                return;

            var allItems = StartupGrid.ItemsSource as List<StartupItem>;
            if (allItems == null) return;

            List<StartupItem> filteredItems;

            switch (StartupCategoryCombo.SelectedIndex)
            {
                case 1: // HKCU
                    filteredItems = allItems.Where(i => i.Location.Contains("HKCU")).ToList();
                    break;
                case 2: // HKLM
                    filteredItems = allItems.Where(i => i.Location.Contains("HKLM")).ToList();
                    break;
                case 3: // Папка
                    filteredItems = allItems.Where(i => i.Location.Contains("Startup") || i.Location.Contains("Common Startup")).ToList();
                    break;
                case 4: // Планировщик
                    filteredItems = allItems.Where(i => i.Type == "Задача").ToList();
                    break;
                case 5: // Службы
                    filteredItems = allItems.Where(i => i.Type == "Служба").ToList();
                    break;
                default: // Все
                    filteredItems = allItems;
                    break;
            }

            StartupGrid.ItemsSource = filteredItems;
            StartupStatusText.Text = $"Отфильтровано: {filteredItems.Count}";
        }

        private void RemoveStartup_Click(object sender, RoutedEventArgs e)
        {
            if (StartupGrid.SelectedItem is StartupItem selectedItem)
            {
                try
                {
                    if (selectedItem.Location.Contains("HKCU"))
                    {
                        using (var key = Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            key?.DeleteValue(selectedItem.Name, false);
                        }
                    }
                    else if (selectedItem.Location.Contains("HKLM"))
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(
                            @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            key?.DeleteValue(selectedItem.Name, false);
                        }
                    }
                    else if (selectedItem.Location.Contains("Startup Folder"))
                    {
                        File.Delete(selectedItem.Path);
                    }

                    LoadAllStartupItems();
                    MessageBox.Show("Элемент автозагрузки удален.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }

        private void ToggleStartup_Click(object sender, RoutedEventArgs e)
        {
            if (StartupGrid.SelectedItem is StartupItem selectedItem)
            {
                try
                {
                    if (selectedItem.Type == "Служба")
                    {
                        using (var service = new ServiceController(selectedItem.Name))
                        {
                            if (service.Status == ServiceControllerStatus.Running)
                            {
                                service.Stop();
                                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                            }
                            else
                            {
                                service.Start();
                                service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                            }
                        }
                    }
                    else if (selectedItem.Location.Contains("HKCU") || selectedItem.Location.Contains("HKLM"))
                    {
                        // Для реестра просто перезагружаем
                        LoadAllStartupItems();
                    }

                    MessageBox.Show("Состояние изменено.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }
        #endregion

        #region Обновленный проводник с функциями удаления/переименования
        public class FileSystemItem
        {
            public string Name { get; set; }
            public string Type { get; set; }
            public string Size { get; set; }
            public string Modified { get; set; }
            public string FullPath { get; set; }
            public bool IsDirectory { get; set; }
        }

        private void InitializeExplorer()
        {
            try
            {
                DriveCombo.Items.Clear();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady)
                    {
                        DriveCombo.Items.Add($"{drive.Name} ({drive.DriveType})");
                    }
                }
                if (DriveCombo.Items.Count > 0)
                    DriveCombo.SelectedIndex = 0;

                // Начинаем с выбора диска
                currentExplorerPath = "";
                ExplorerPathBox.Text = "Выберите диск для начала работы";
                ExplorerGrid.ItemsSource = new List<FileSystemItem>();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации проводника: {ex.Message}");
            }
        }

        private void LoadDirectory(string path)
        {
            try
            {
                currentExplorerPath = path;
                ExplorerPathBox.Text = path;

                var items = new List<FileSystemItem>();

                // Добавляем ".." для перехода на уровень выше
                if (!IsRootDirectory(path))
                {
                    items.Add(new FileSystemItem
                    {
                        Name = "..",
                        Type = "Родительская папка",
                        Size = "",
                        Modified = "",
                        FullPath = Directory.GetParent(path)?.FullName,
                        IsDirectory = true
                    });
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    items.Add(new FileSystemItem
                    {
                        Name = dirInfo.Name,
                        Type = "Папка",
                        Size = "",
                        Modified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        FullPath = dir,
                        IsDirectory = true
                    });
                }

                foreach (var file in Directory.GetFiles(path))
                {
                    var fileInfo = new FileInfo(file);
                    items.Add(new FileSystemItem
                    {
                        Name = fileInfo.Name,
                        Type = fileInfo.Extension.ToUpper() + " файл",
                        Size = FormatFileSize(fileInfo.Length),
                        Modified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        FullPath = file,
                        IsDirectory = false
                    });
                }

                ExplorerGrid.ItemsSource = items;
                ExplorerStatus.Text = $"Папка: {path} | Элементов: {items.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки директории: {ex.Message}");
            }
        }

        private bool IsRootDirectory(string path)
        {
            return Path.GetPathRoot(path) == path;
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void ExplorerDelete_Click(object sender, RoutedEventArgs e)
        {
            if (ExplorerGrid.SelectedItem is FileSystemItem selectedItem)
            {
                try
                {
                    var result = MessageBox.Show($"Вы уверены, что хотите удалить '{selectedItem.Name}'?",
                        "Подтверждение удаления", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (selectedItem.IsDirectory)
                        {
                            if (selectedItem.Name == "..") return;

                            // Используем SHFileOperation для удаления с корзиной
                            var shf = new SHFILEOPSTRUCT
                            {
                                wFunc = FO_DELETE,
                                pFrom = selectedItem.FullPath + '\0' + '\0',
                                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
                            };
                            SHFileOperation(ref shf);
                        }
                        else
                        {
                            // Для файлов тоже используем корзину
                            var shf = new SHFILEOPSTRUCT
                            {
                                wFunc = FO_DELETE,
                                pFrom = selectedItem.FullPath + '\0' + '\0',
                                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION
                            };
                            SHFileOperation(ref shf);
                        }

                        // Обновляем список
                        Task.Delay(500).ContinueWith(_ =>
                        {
                            Dispatcher.Invoke(() => LoadDirectory(currentExplorerPath));
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка удаления: {ex.Message}");
                }
            }
        }

        private void ExplorerRename_Click(object sender, RoutedEventArgs e)
        {
            if (ExplorerGrid.SelectedItem is FileSystemItem selectedItem)
            {
                if (selectedItem.Name == "..") return;

                var dialog = new InputDialog("Введите новое имя:", selectedItem.Name);
                if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Answer))
                {
                    try
                    {
                        string newName = dialog.Answer;
                        string newPath = Path.Combine(Path.GetDirectoryName(selectedItem.FullPath), newName);

                        if (selectedItem.IsDirectory)
                        {
                            Directory.Move(selectedItem.FullPath, newPath);
                        }
                        else
                        {
                            File.Move(selectedItem.FullPath, newPath);
                        }

                        LoadDirectory(currentExplorerPath);
                        MessageBox.Show("Переименование выполнено успешно.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка переименования: {ex.Message}");
                    }
                }
            }
        }

        private void ExplorerNewFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Введите имя новой папки:", "Новая папка");
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Answer))
            {
                try
                {
                    string newFolderPath = Path.Combine(currentExplorerPath, dialog.Answer);
                    Directory.CreateDirectory(newFolderPath);
                    LoadDirectory(currentExplorerPath);
                    MessageBox.Show("Папка создана успешно.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка создания папки: {ex.Message}");
                }
            }
        }

        private void ExplorerBack_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(currentExplorerPath) && Directory.Exists(currentExplorerPath))
            {
                var parent = Directory.GetParent(currentExplorerPath);
                if (parent != null)
                {
                    LoadDirectory(parent.FullName);
                }
            }
        }

        private void ExplorerUp_Click(object sender, RoutedEventArgs e)
        {
            ExplorerBack_Click(sender, e);
        }

        private void ExplorerGo_Click(object sender, RoutedEventArgs e)
        {
            if (Directory.Exists(ExplorerPathBox.Text))
            {
                LoadDirectory(ExplorerPathBox.Text);
            }
            else
            {
                MessageBox.Show("Директория не существует");
            }
        }

        private void ExplorerPathBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                ExplorerGo_Click(sender, e);
            }
        }

        private void DriveCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DriveCombo.SelectedItem != null)
            {
                string driveText = DriveCombo.SelectedItem.ToString();
                string drivePath = driveText.Split(' ')[0];
                if (Directory.Exists(drivePath))
                {
                    LoadDirectory(drivePath);
                }
            }
        }

        private void ExplorerGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ExplorerGrid.SelectedItem is FileSystemItem selectedItem)
            {
                if (selectedItem.IsDirectory)
                {
                    if (selectedItem.Name == "..")
                    {
                        ExplorerBack_Click(sender, e);
                    }
                    else
                    {
                        LoadDirectory(selectedItem.FullPath);
                    }
                }
                else
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = selectedItem.FullPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка открытия файла: {ex.Message}");
                    }
                }
            }
        }

        private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExplorerGrid.SelectedItem is FileSystemItem selectedItem)
            {
                SelectedItemInfo.Text = $"Выбрано: {selectedItem.Name} | Размер: {selectedItem.Size}";
            }
            else
            {
                SelectedItemInfo.Text = "";
            }
        }
        #endregion

        #region Обновленный диспетчер задач
        public class ProcessInfo
        {
            public int Id { get; set; }
            public string ProcessName { get; set; }
            public double MemoryMB { get; set; }
            public string Priority { get; set; }
            public string Path { get; set; }
        }

        private void LoadProcesses()
        {
            try
            {
                var processes = Process.GetProcesses()
                    .Select(p =>
                    {
                        try
                        {
                            return new ProcessInfo
                            {
                                Id = p.Id,
                                ProcessName = p.ProcessName,
                                MemoryMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 2),
                                Priority = p.PriorityClass.ToString(),
                                Path = p.MainModule?.FileName ?? "N/A"
                            };
                        }
                        catch
                        {
                            return new ProcessInfo
                            {
                                Id = p.Id,
                                ProcessName = p.ProcessName,
                                MemoryMB = Math.Round(p.WorkingSet64 / 1024.0 / 1024.0, 2),
                                Priority = "Access Denied",
                                Path = "Access Denied"
                            };
                        }
                    })
                    .OrderByDescending(p => p.MemoryMB)
                    .ToList();

                ProcessesGrid.ItemsSource = processes;
                ProcessCountText.Text = $"Всего процессов: {processes.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки процессов: {ex.Message}");
            }
        }

        private void RefreshProcesses_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void KillProcess_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessesGrid.SelectedItem is ProcessInfo selectedProcess)
            {
                try
                {
                    var process = Process.GetProcessById(selectedProcess.Id);
                    process.Kill();
                    process.WaitForExit(5000);
                    LoadProcesses();
                    MessageBox.Show($"Процесс {selectedProcess.ProcessName} завершен.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка завершения процесса: {ex.Message}");
                }
            }
        }

        private void SearchProcessBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (ProcessesGrid.ItemsSource is List<ProcessInfo> processes)
            {
                var searchText = SearchProcessBox.Text.ToLower();
                var filtered = processes.Where(p => p.ProcessName.ToLower().Contains(searchText)).ToList();
                ProcessesGrid.ItemsSource = filtered;
                ProcessCountText.Text = $"Найдено: {filtered.Count}";
            }
        }
        #endregion

        #region Анти-дебаг функции
        private void CheckDebuggers_Click(object sender, RoutedEventArgs e)
        {
            bool isDebuggerPresent = IsDebuggerPresent();
            bool remoteDebuggerPresent = false;
            CheckRemoteDebuggerPresent(Process.GetCurrentProcess().Handle, ref remoteDebuggerPresent);

            DebugStatusText.Text = $"Локальный дебаггер: {(isDebuggerPresent ? "ОБНАРУЖЕН" : "Не найден")}\n" +
                                  $"Удаленный дебаггер: {(remoteDebuggerPresent ? "ОБНАРУЖЕН" : "Не найден")}\n" +
                                  $"Время: {DateTime.Now:HH:mm:ss}";
        }

        private void RemoveDebuggers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var debugProcesses = Process.GetProcesses()
                    .Where(p => p.ProcessName.ToLower().Contains("debug") ||
                               p.ProcessName.ToLower().Contains("olly") ||
                               p.ProcessName.ToLower().Contains("ida") ||
                               p.ProcessName.ToLower().Contains("x64dbg"))
                    .ToList();

                foreach (var process in debugProcesses)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                }

                DebugStatusText.Text += $"\nПопытка завершения {debugProcesses.Count} дебаггеров завершена.";
            }
            catch (Exception ex)
            {
                DebugStatusText.Text += $"\nОшибка: {ex.Message}";
            }
        }

        private void CheckVirtualization_Click(object sender, RoutedEventArgs e)
        {
            bool isVirtualized = false;
            string[] vmIndicators = { "VMware", "VirtualBox", "Hyper-V", "QEMU", "Xen" };

            try
            {
                var computerSystem = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem")
                    .Get()
                    .OfType<System.Management.ManagementObject>()
                    .FirstOrDefault();

                if (computerSystem != null)
                {
                    string manufacturer = computerSystem["Manufacturer"]?.ToString() ?? "";
                    string model = computerSystem["Model"]?.ToString() ?? "";

                    foreach (var indicator in vmIndicators)
                    {
                        if (manufacturer.Contains(indicator) || model.Contains(indicator))
                        {
                            isVirtualized = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            VirtualizationStatus.Text = $"Виртуализация: {(isVirtualized ? "ОБНАРУЖЕНА" : "Не обнаружена")}\n" +
                                       $"Проверка выполнена: {DateTime.Now:HH:mm:ss}";
        }
        #endregion

        #region Функции реестра
        private void ViewRunKeys_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var runKeys = new List<string>();

                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        runKeys.Add("HKCU Run:");
                        foreach (var valueName in key.GetValueNames())
                        {
                            runKeys.Add($"  {valueName} = {key.GetValue(valueName)}");
                        }
                    }
                }

                using (var key = Registry.LocalMachine.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (key != null)
                    {
                        runKeys.Add("\nHKLM Run:");
                        foreach (var valueName in key.GetValueNames())
                        {
                            runKeys.Add($"  {valueName} = {key.GetValue(valueName)}");
                        }
                    }
                }

                RunKeysTextBox.Text = string.Join("\n", runKeys);
            }
            catch (Exception ex)
            {
                RunKeysTextBox.Text = $"Ошибка: {ex.Message}";
            }
        }

        private void RemoveFromRun_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Введите имя ключа для удаления:");
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Answer))
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        key?.DeleteValue(dialog.Answer, false);
                    }

                    using (var key = Registry.LocalMachine.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        key?.DeleteValue(dialog.Answer, false);
                    }

                    MessageBox.Show("Ключ удален (если существовал).");
                    ViewRunKeys_Click(sender, e);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }
        #endregion

        #region Winlogon функции
        public class WinlogonItem
        {
            public string Key { get; set; }
            public string Value { get; set; }
            public string Type { get; set; }
            public string Section { get; set; }
        }

        private void LoadWinlogon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var winlogonItems = new List<WinlogonItem>();

                LoadWinlogonSection(winlogonItems, Registry.LocalMachine,
                    @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "HKLM Winlogon");

                LoadWinlogonSection(winlogonItems, Registry.CurrentUser,
                    @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", "HKCU Winlogon");

                WinlogonGrid.ItemsSource = winlogonItems;
                WinlogonStatus.Text = $"Загружено ключей: {winlogonItems.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки Winlogon: {ex.Message}");
            }
        }

        private void LoadWinlogonSection(List<WinlogonItem> items, RegistryKey root, string subKey, string section)
        {
            try
            {
                using (var key = root.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            var value = key.GetValue(valueName)?.ToString();
                            if (!string.IsNullOrEmpty(value))
                            {
                                items.Add(new WinlogonItem
                                {
                                    Key = valueName,
                                    Value = value,
                                    Type = key.GetValueKind(valueName).ToString(),
                                    Section = section
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void RemoveWinlogonKey_Click(object sender, RoutedEventArgs e)
        {
            if (WinlogonGrid.SelectedItem is WinlogonItem selectedItem)
            {
                try
                {
                    RegistryKey root = selectedItem.Section.Contains("HKLM") ?
                        Registry.LocalMachine : Registry.CurrentUser;

                    using (var key = root.OpenSubKey(
                        @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                    {
                        key?.DeleteValue(selectedItem.Key, false);
                    }

                    LoadWinlogon_Click(sender, e);
                    MessageBox.Show("Ключ Winlogon удален.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }

        private void AddWinlogonKey_Click(object sender, RoutedEventArgs e)
        {
            var keyDialog = new InputDialog("Введите имя ключа:");
            if (keyDialog.ShowDialog() == true && !string.IsNullOrEmpty(keyDialog.Answer))
            {
                var valueDialog = new InputDialog("Введите значение ключа:");
                if (valueDialog.ShowDialog() == true && !string.IsNullOrEmpty(valueDialog.Answer))
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(
                            @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon", true))
                        {
                            key?.SetValue(keyDialog.Answer, valueDialog.Answer);
                        }

                        LoadWinlogon_Click(sender, e);
                        MessageBox.Show("Ключ Winlogon добавлен.");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Ошибка: {ex.Message}");
                    }
                }
            }
        }
        #endregion

        #region CMDLine функции
        public class CMDCommand
        {
            public string Name { get; set; }
            public string Command { get; set; }
            public string Location { get; set; }
        }

        private void CheckCMDLine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var commands = new List<CMDCommand>();

                // AutoRun из реестра
                LoadCMDAutoRun(commands, Registry.CurrentUser,
                    @"Software\Microsoft\Command Processor", "HKCU AutoRun");

                LoadCMDAutoRun(commands, Registry.LocalMachine,
                    @"Software\Microsoft\Command Processor", "HKLM AutoRun");

                CMDLineGrid.ItemsSource = commands;
                CMDLineStatus.Text = $"Найдено команд: {commands.Count}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка проверки CMD: {ex.Message}");
            }
        }

        private void LoadCMDAutoRun(List<CMDCommand> commands, RegistryKey root, string subKey, string location)
        {
            try
            {
                using (var key = root.OpenSubKey(subKey))
                {
                    if (key != null)
                    {
                        var autoRunValue = key.GetValue("AutoRun")?.ToString();
                        if (!string.IsNullOrEmpty(autoRunValue))
                        {
                            commands.Add(new CMDCommand
                            {
                                Name = "AutoRun",
                                Command = autoRunValue,
                                Location = location
                            });
                        }
                    }
                }
            }
            catch { }
        }

        private void RemoveCMDCommand_Click(object sender, RoutedEventArgs e)
        {
            if (CMDLineGrid.SelectedItem is CMDCommand selectedCommand)
            {
                try
                {
                    RegistryKey root = selectedCommand.Location.Contains("HKLM") ?
                        Registry.LocalMachine : Registry.CurrentUser;

                    using (var key = root.OpenSubKey(
                        @"Software\Microsoft\Command Processor", true))
                    {
                        key?.DeleteValue("AutoRun", false);
                    }

                    CheckCMDLine_Click(sender, e);
                    MessageBox.Show("Команда AutoRun удалена.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }

        private void AddCMDCommand_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Введите команду для AutoRun:");
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Answer))
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Command Processor", true))
                    {
                        key?.SetValue("AutoRun", dialog.Answer);
                    }

                    CheckCMDLine_Click(sender, e);
                    MessageBox.Show("Команда AutoRun добавлена.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка: {ex.Message}");
                }
            }
        }
        #endregion

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _moduleManager.UnloadAllModules();
        }
    }
}