using CommunityToolkit.Mvvm.Input;
using ControlCalidadQR.Models;

namespace ControlCalidadQR.PageModels
{
    public interface IProjectTaskPageModel
    {
        IAsyncRelayCommand<ProjectTask> NavigateToTaskCommand { get; }
        bool IsBusy { get; }
    }
}