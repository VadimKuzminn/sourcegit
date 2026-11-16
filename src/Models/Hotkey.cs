using System;
using System.Text;
using Avalonia.Input;

namespace SourceGit.Models
{
    /// <summary>
    ///     Where a hotkey is active. Two hotkeys can only conflict when their scopes overlap: `Global` overlaps
    ///     everything, `Welcome` and `Repository` never overlap each other.
    /// </summary>
    public enum HotkeyScope
    {
        Global,
        Welcome,
        Repository,
    }

    /// <summary>
    ///     Section of the hotkey editor a command is listed in.
    /// </summary>
    public enum HotkeyGroup
    {
        Global,
        Repository,
        History,
        LocalChanges,
        Diff,
        Welcome,
    }

    public class HotkeyDefinition
    {
        public string Id { get; }
        public HotkeyGroup Group { get; }
        public HotkeyScope Scope { get; }
        public string LabelKey { get; }
        public KeyGesture Default { get; }

        /// <summary>
        ///     True when `Views.Launcher` matches and runs this command itself. Other commands are bound to a
        ///     `Button.HotKey` (or handled by a view) and only read their gesture from `ViewModels.HotkeyManager`.
        /// </summary>
        public bool HandledByLauncher { get; }

        public HotkeyDefinition(string id, HotkeyGroup group, HotkeyScope scope, string labelKey, KeyGesture def, bool handledByLauncher)
        {
            Id = id;
            Group = group;
            Scope = scope;
            LabelKey = labelKey;
            Default = def;
            HandledByLauncher = handledByLauncher;
        }

        public bool OverlapsWith(HotkeyDefinition other)
        {
            return Scope == HotkeyScope.Global || other.Scope == HotkeyScope.Global || Scope == other.Scope;
        }
    }

    public static class HotkeyGesture
    {
        /// <summary>
        ///     `Ctrl` on Windows/Linux, `⌘` on macOS.
        /// </summary>
        public static KeyModifiers Cmd => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        public static KeyGesture Create(Key key, KeyModifiers modifiers = KeyModifiers.None)
        {
            return new KeyGesture(Normalize(key), modifiers);
        }

        public static bool AreEqual(KeyGesture a, KeyGesture b)
        {
            if (a == null || b == null)
                return a == null && b == null;

            return Normalize(a.Key) == Normalize(b.Key) && a.KeyModifiers == b.KeyModifiers;
        }

        public static bool Matches(KeyGesture gesture, KeyEventArgs e)
        {
            return gesture != null && Normalize(e.Key) == Normalize(gesture.Key) && e.KeyModifiers == gesture.KeyModifiers;
        }

        /// <summary>
        ///     Number keys on the numpad behave like the ones in the main block (`Ctrl+1` also fires on `Ctrl+Num1`).
        /// </summary>
        public static Key Normalize(Key key)
        {
            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return Key.D0 + (key - Key.NumPad0);

            return key;
        }

        public static bool IsModifierKey(Key key)
        {
            return key is Key.LeftCtrl or Key.RightCtrl or
                Key.LeftShift or Key.RightShift or
                Key.LeftAlt or Key.RightAlt or
                Key.LWin or Key.RWin or
                Key.System or Key.None or
                Key.ImeProcessed or Key.DeadCharProcessed;
        }

        public static bool IsFunctionKey(Key key)
        {
            return key >= Key.F1 && key <= Key.F24;
        }

        /// <summary>
        ///     A gesture is usable as an application hotkey when it can not be confused with typing: it either uses
        ///     Ctrl/Alt/⌘ or is a function key.
        /// </summary>
        public static bool IsAssignable(KeyGesture gesture)
        {
            if (gesture == null || IsModifierKey(gesture.Key))
                return false;

            if (IsFunctionKey(gesture.Key))
                return true;

            return (gesture.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != KeyModifiers.None;
        }

        /// <summary>
        ///     Invariant form stored in preferences, e.g. `Ctrl+Shift+OemComma`.
        /// </summary>
        public static string Serialize(KeyGesture gesture)
        {
            if (gesture == null)
                return string.Empty;

            var builder = new StringBuilder();
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
                builder.Append("Ctrl+");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
                builder.Append("Alt+");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
                builder.Append("Shift+");
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
                builder.Append("Meta+");

            builder.Append(Normalize(gesture.Key).ToString());
            return builder.ToString();
        }

        public static KeyGesture Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                return null;

            var modifiers = KeyModifiers.None;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl" or "control":
                        modifiers |= KeyModifiers.Control;
                        break;
                    case "alt":
                        modifiers |= KeyModifiers.Alt;
                        break;
                    case "shift":
                        modifiers |= KeyModifiers.Shift;
                        break;
                    case "meta" or "cmd" or "win":
                        modifiers |= KeyModifiers.Meta;
                        break;
                    default:
                        return null;
                }
            }

            if (!Enum.TryParse<Key>(parts[^1], true, out var key) || IsModifierKey(key))
                return null;

            return Create(key, modifiers);
        }

        /// <summary>
        ///     Text shown to the user, e.g. `Ctrl+Shift+,` or `⌘+⇧+,`.
        /// </summary>
        public static string Format(KeyGesture gesture)
        {
            if (gesture == null)
                return string.Empty;

            var builder = new StringBuilder();
            var modifiers = gesture.KeyModifiers;
            if (OperatingSystem.IsMacOS())
            {
                if (modifiers.HasFlag(KeyModifiers.Meta))
                    builder.Append("⌘+");
                if (modifiers.HasFlag(KeyModifiers.Control))
                    builder.Append("⌃+");
                if (modifiers.HasFlag(KeyModifiers.Alt))
                    builder.Append("⌥+");
                if (modifiers.HasFlag(KeyModifiers.Shift))
                    builder.Append("⇧+");
            }
            else
            {
                if (modifiers.HasFlag(KeyModifiers.Control))
                    builder.Append("Ctrl+");
                if (modifiers.HasFlag(KeyModifiers.Alt))
                    builder.Append("Alt+");
                if (modifiers.HasFlag(KeyModifiers.Shift))
                    builder.Append("Shift+");
                if (modifiers.HasFlag(KeyModifiers.Meta))
                    builder.Append(OperatingSystem.IsWindows() ? "Win+" : "Super+");
            }

            builder.Append(FormatKey(gesture.Key));
            return builder.ToString();
        }

        private static string FormatKey(Key key)
        {
            key = Normalize(key);
            if (key >= Key.D0 && key <= Key.D9)
                return ((char)('0' + (key - Key.D0))).ToString();

            return key switch
            {
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.OemPlus => "=",
                Key.OemMinus => "-",
                Key.OemTilde => "`",
                Key.OemQuestion => "/",
                Key.OemSemicolon => ";",
                Key.OemQuotes => "'",
                Key.OemOpenBrackets => "[",
                Key.OemCloseBrackets => "]",
                Key.OemPipe or Key.OemBackslash => "\\",
                Key.Add => "Num +",
                Key.Subtract => "Num -",
                Key.Multiply => "Num *",
                Key.Divide => "Num /",
                Key.Decimal => "Num .",
                Key.Enter => "Enter",
                Key.Escape => "Esc",
                Key.Back => "Backspace",
                Key.PageUp => "PageUp",
                Key.PageDown => "PageDown",
                _ => key.ToString(),
            };
        }
    }
}
