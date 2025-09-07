using System;
using System.Windows;

namespace SystemAnalyzer
{
    public partial class InputDialog : Window
    {
        public string Answer { get; set; }

        public InputDialog(string question, string defaultAnswer = "")
        {
            InitializeComponent();
            lblQuestion.Content = question;
            txtAnswer.Text = defaultAnswer;
        }

        private void btnDialogOk_Click(object sender, RoutedEventArgs e)
        {
            Answer = txtAnswer.Text;
            DialogResult = true;
        }

        private void btnDialogCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}