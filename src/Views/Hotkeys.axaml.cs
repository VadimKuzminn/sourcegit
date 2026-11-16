using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace SourceGit.Views
{
    public partial class Hotkeys : ChromelessWindow
    {
        public Hotkeys()
        {
            CloseOnESC = true;
            DataContext = new ViewModels.HotkeyEditor();
            InitializeComponent();

            // Tunnel, so the key being recorded never reaches buttons, text boxes or the ESC/zoom handlers.
            AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        }

        protected override void OnOpened(System.EventArgs e)
        {
            base.OnOpened(e);
            TxtFilter.Focus();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == IsActiveProperty && !IsActive && DataContext is ViewModels.HotkeyEditor vm)
            {
                vm.CancelRecording();
                vm.CancelRecordingFilter();
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ViewModels.HotkeyEditor vm)
                return;

            if (vm.IsCapturingKeys)
            {
                e.Handled = vm.OnKeyPressed(e.Key, e.KeyModifiers);
            }
            else if (vm.IsConfirmingReassign && e.KeyModifiers == KeyModifiers.None)
            {
                if (e.Key is Key.Enter)
                {
                    vm.ConfirmReassign();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    vm.CancelReassign();
                    e.Handled = true;
                }
            }
        }

        private void OnToggleRecordFilter(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HotkeyEditor vm && sender is Button button)
            {
                if (vm.IsRecordingFilter)
                {
                    vm.CancelRecordingFilter();
                }
                else
                {
                    button.Focus(NavigationMethod.Pointer);
                    vm.StartRecordingFilter();
                }
            }

            e.Handled = true;
        }

        private void OnFilterRecorderLostFocus(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.HotkeyEditor)?.CancelRecordingFilter();
        }

        private void OnClearFilterGesture(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.HotkeyEditor)?.ClearFilterGesture();
            e.Handled = true;
        }

        private void OnConfirmReassign(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.HotkeyEditor)?.ConfirmReassign();
            e.Handled = true;
        }

        private void OnCancelReassign(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.HotkeyEditor)?.CancelReassign();
            e.Handled = true;
        }

        private void OnClearFilter(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.HotkeyEditor)?.ClearFilter();
            e.Handled = true;
        }

        private void OnResetAll(object sender, RoutedEventArgs e)
        {
            (DataContext as ViewModels.HotkeyEditor)?.ResetAll();
            e.Handled = true;
        }

        private void OnStartRecording(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HotkeyEditor vm &&
                sender is Button { DataContext: ViewModels.HotkeyEditorItem item } button)
            {
                if (item.IsRecording)
                {
                    vm.CancelRecording();
                }
                else
                {
                    button.Focus(NavigationMethod.Pointer);
                    vm.StartRecording(item);
                }
            }

            e.Handled = true;
        }

        private void OnRecorderLostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HotkeyEditor { Recording: { } recording } vm &&
                sender is Button { DataContext: ViewModels.HotkeyEditorItem item } &&
                item == recording)
                vm.CancelRecording();
        }

        private void OnResetItem(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HotkeyEditor vm && sender is Button { DataContext: ViewModels.HotkeyEditorItem item })
                vm.Reset(item);

            e.Handled = true;
        }

        private void OnClearItem(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.HotkeyEditor vm && sender is Button { DataContext: ViewModels.HotkeyEditorItem item })
                vm.Clear(item);

            e.Handled = true;
        }
    }
}
