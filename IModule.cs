using System.Windows.Controls;

namespace SystemAnalyzer
{
    public interface IModule
    {
        string ModuleName { get; }
        string ModuleDescription { get; }
        UserControl ModuleInterface { get; }
        void Initialize();
        void Dispose();
    }
}