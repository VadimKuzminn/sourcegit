using System;

using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;

namespace SourceGit.Views
{
    /// <summary>
    ///     `HotKey="{v:Hotkey Repo.Fetch}"` binds a gesture to the (user editable) hotkey of a command and follows
    ///     every change made in the hotkey editor.
    /// </summary>
    public class HotkeyExtension
    {
        public string Id { get; set; }

        public HotkeyExtension()
        {
        }

        public HotkeyExtension(string id)
        {
            Id = id;
        }

        public IBinding ProvideValue(IServiceProvider serviceProvider)
        {
            return new Binding(nameof(ViewModels.HotkeyManager.Revision))
            {
                Source = ViewModels.HotkeyManager.Instance,
                Mode = BindingMode.OneWay,
                Converter = s_toGesture,
                ConverterParameter = Id,
            };
        }

        private static readonly FuncValueConverter<int, string, KeyGesture> s_toGesture =
            new((_, id) => ViewModels.HotkeyManager.Instance.Get(id));
    }

    /// <summary>
    ///     `Text="{v:HotkeyText Repo.Fetch}"` shows the hotkey of a command (empty when it has none).
    /// </summary>
    public class HotkeyTextExtension
    {
        public string Id { get; set; }

        public HotkeyTextExtension()
        {
        }

        public HotkeyTextExtension(string id)
        {
            Id = id;
        }

        public IBinding ProvideValue(IServiceProvider serviceProvider)
        {
            return new Binding(nameof(ViewModels.HotkeyManager.Revision))
            {
                Source = ViewModels.HotkeyManager.Instance,
                Mode = BindingMode.OneWay,
                Converter = s_toText,
                ConverterParameter = Id,
            };
        }

        private static readonly FuncValueConverter<int, string, string> s_toText =
            new((_, id) => ViewModels.HotkeyManager.Instance.GetText(id));
    }
}
