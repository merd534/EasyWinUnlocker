using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SystemAnalyzer
{
    public class ModuleManager
    {
        private readonly List<IModule> _loadedModules = new List<IModule>();

        public IReadOnlyList<IModule> LoadedModules => _loadedModules.AsReadOnly();

        public void LoadAllModules()
        {
            try
            {
                // Загрузка модулей из текущей сборки
                var moduleTypes = Assembly.GetExecutingAssembly()
                    .GetTypes()
                    .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(BaseModule));

                foreach (var type in moduleTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IModule module)
                        {
                            module.Initialize();
                            _loadedModules.Add(module);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка загрузки модуля {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при загрузке модулей: {ex.Message}");
            }
        }

        public IModule GetModuleByName(string moduleName)
        {
            return _loadedModules.FirstOrDefault(m => m.ModuleName.Equals(moduleName, StringComparison.OrdinalIgnoreCase));
        }

        public void UnloadAllModules()
        {
            foreach (var module in _loadedModules)
            {
                try
                {
                    module.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка выгрузки модуля {module.ModuleName}: {ex.Message}");
                }
            }
            _loadedModules.Clear();
        }
    }
}