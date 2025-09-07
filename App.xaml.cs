using System;
using System.Windows;

namespace SystemAnalyzer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                MessageBox.Show($"Необработанное исключение: {args.ExceptionObject}");
            };

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"Ошибка UI: {args.Exception.Message}");
                args.Handled = true;
            };
        }
    }
}