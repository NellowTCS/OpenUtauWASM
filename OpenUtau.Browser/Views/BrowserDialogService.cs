using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace OpenUtau.App.Views {
    public class BrowserDialogService {
        private readonly Panel modalPanel;

        public BrowserDialogService(Panel modalPanel) {
            this.modalPanel = modalPanel;
        }

        public async Task<T?> ShowDialog<T>(UserControl dialog, Action<T?>? onClose = null) where T : class {
            var result = default(T);
            var overlay = new Border {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#80000000")),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                ZIndex = 1000,
            };

            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;

            overlay.Child = dialog;
            modalPanel.Children.Add(overlay);

            await Task.Run(() => { });

            return result;
        }
    }
}
