using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtau.App.ViewModels;

namespace OpenUtau.App.Views {
    public partial class MainWindow : UserControl, IMainWindow, IMainWindowMarker {
        public MainWindow() {
            InitializeComponent();
        }

        public void InitProject() {
        }

        public Task OpenSingersWindowAsync() {
            return Task.CompletedTask;
        }

        public void SetPianoRollAttachment() {
        }

        public Task Save() {
            return Task.CompletedTask;
        }
    }
}
