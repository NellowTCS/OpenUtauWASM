using System.Threading.Tasks;

namespace OpenUtau.App.ViewModels {
    public interface IMainWindow {
        void InitProject();
        Task OpenSingersWindowAsync();
        void SetPianoRollAttachment();
        Task Save();
    }
}
