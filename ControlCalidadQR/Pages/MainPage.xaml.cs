using ControlCalidadQR.Models;
using ControlCalidadQR.PageModels;

namespace ControlCalidadQR.Pages
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageModel model)
        {
            InitializeComponent();
            BindingContext = model;
        }
    }
}