using PresenceSync.Core.Resources;
using System.Windows.Interop;
using PresenceSync.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace PresenceSync.App.Views;

internal partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        vm.OwnerHandle = () => new WindowInteropHelper(this).Handle;
        vm.CloseRequested += (_, _) => Close();
        vm.ConfirmAsync = async (title, text) =>
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Owner = this,
                Title = title,
                Content = new System.Windows.Controls.TextBlock { Text = text, TextWrapping = System.Windows.TextWrapping.Wrap, MaxWidth = 460 },
                PrimaryButtonText = Strings.Replace_Button,
                PrimaryButtonAppearance = ControlAppearance.Caution,
                CloseButtonText = Strings.Common_Cancel,
            };
            return await box.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary;
        };
        SourceInitialized += (_, _) => SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, true);
    }
}
