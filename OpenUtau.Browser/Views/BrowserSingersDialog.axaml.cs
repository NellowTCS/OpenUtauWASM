using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views
{
    public partial class BrowserSingersDialog : Window
    {
        public BrowserSingersDialog()
        {
            InitializeComponent();
            DataContext = new SingersViewModel();
        }

        private void OnClose(object? sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
