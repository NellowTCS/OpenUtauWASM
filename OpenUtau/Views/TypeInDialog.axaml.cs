using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class TypeInDialog : Window {
        public TypeInDialogViewModel ViewModel => (TypeInDialogViewModel)DataContext!;

        public Action<string>? onFinish {
            get => ViewModel.OnFinish;
            set => ViewModel.OnFinish = value;
        }

        public TypeInDialog() {
            InitializeComponent();
            DataContext = new TypeInDialogViewModel();
            OkButton.Click += OkButtonClick;
            TextBox.AttachedToVisualTree += (s, e) => { TextBox.SelectAll(); TextBox.Focus(); };
        }

        public void SetPrompt(string prompt) {
            ViewModel.Prompt = prompt;
        }

        public void SetText(string text) {
            ViewModel.Text = text;
        }

        public void SetTitle(string title) {
            ViewModel.Title = title;
        }

        private void OkButtonClick(object? sender, RoutedEventArgs e) {
            ViewModel.Finish();
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Close();
            } else if (e.Key == Key.Enter) {
                e.Handled = true;
                ViewModel.Finish();
                Close();
            } else {
                base.OnKeyDown(e);
            }
        }
    }
}
