using System;
using ReactiveUI;

namespace OpenUtau.App.ViewModels {
    public class TypeInDialogViewModel : ReactiveObject {
        private string prompt = string.Empty;
        public string Prompt {
            get => prompt;
            set => this.RaiseAndSetIfChanged(ref prompt, value);
        }

        private string text = string.Empty;
        public string Text {
            get => text;
            set => this.RaiseAndSetIfChanged(ref text, value);
        }

        private string title = "TypeInDialog";
        public string Title {
            get => title;
            set => this.RaiseAndSetIfChanged(ref title, value);
        }

        public Action<string>? OnFinish { get; set; }

        public void Finish() {
            OnFinish?.Invoke(Text ?? string.Empty);
        }
    }
}
