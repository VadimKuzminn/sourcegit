using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class SSHKeyHelper : ChromelessWindow
    {
        /// <summary>
        ///     Shows the helper as a key picker. Returns the private key path that was checked (an empty string
        ///     when no key is checked), or null if the user cancelled.
        /// </summary>
        public static async Task<string> PickAsync(Control control, string currentKey)
        {
            if (TopLevel.GetTopLevel(control) is not Window owner)
                return null;

            var dialog = new SSHKeyHelper() { DataContext = new ViewModels.SSHKeyHelper(currentKey) };
            return await dialog.ShowDialog<string>(owner);
        }

        public SSHKeyHelper()
        {
            InitializeComponent();
        }

        private void OnAddNewKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SSHKeyHelper vm)
                return;

            vm.OpenGenerator();
            e.Handled = true;
        }

        private void OnGenerateKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SSHKeyHelper vm)
                return;

            vm.Generate();
            e.Handled = true;
        }

        private void OnCancelGenerateKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SSHKeyHelper vm)
                return;

            vm.CloseGenerator();
            e.Handled = true;
        }

        private async void OnDeleteSelectedKey(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SSHKeyHelper { SelectedKey: { } key } vm)
                return;

            var message = new StringBuilder();
            message
                .AppendLine(App.Text("SSHKeyHelper.ConfirmDeletion"))
                .AppendLine()
                .Append("• ").Append(key.PrivateKeyPath).AppendLine()
                .Append("• ").Append(key.PublicKeyPath);

            var confirm = new Confirm();
            confirm.SetData(message.ToString(), Models.ConfirmButtonType.YesNo);

            var yes = await confirm.ShowDialog<bool>(this);
            if (yes)
                vm.DeleteSelected();

            e.Handled = true;
        }

        private void OnKeyCheckClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SSHKeyHelper vm)
                return;

            if (sender is CheckBox { DataContext: Models.SSHKeyPair key } checkBox)
            {
                vm.SetKeyChecked(key, checkBox.IsChecked == true);
                vm.SelectedKey = key;
            }

            e.Handled = true;
        }

        private void OnKeyDoubleTapped(object sender, TappedEventArgs e)
        {
            if (DataContext is not ViewModels.SSHKeyHelper { IsPickerMode: true } vm)
                return;

            if (sender is Control { DataContext: Models.SSHKeyPair key })
            {
                vm.SetKeyChecked(key, true);
                Close(key.PrivateKeyPath);
            }

            e.Handled = true;
        }

        private async void OnCopyPublicKey(object sender, RoutedEventArgs e)
        {
            if (sender is Control { DataContext: Models.SSHKeyPair key } && !string.IsNullOrEmpty(key.RawPublicKey))
                await this.CopyTextAsync(key.RawPublicKey);

            e.Handled = true;
        }

        private void OnSure(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SSHKeyHelper vm)
                Close(vm.CheckedKey?.PrivateKeyPath ?? string.Empty);

            e.Handled = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close();
            e.Handled = true;
        }
    }
}
