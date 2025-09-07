using System.Windows.Controls;

namespace SystemAnalyzer
{
    public abstract class BaseModule : IModule
    {
        public abstract string ModuleName { get; }
        public abstract string ModuleDescription { get; }
        public abstract UserControl ModuleInterface { get; }

        public virtual void Initialize()
        {
            // Базовая реализация инициализации
        }

        public virtual void Dispose()
        {
            // Базовая реализация очистки
        }
    }
}