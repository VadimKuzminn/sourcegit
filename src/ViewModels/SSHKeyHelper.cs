using System;
using System.Collections.Generic;
using System.IO;

using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class SSHKeyHelper : ObservableObject
    {
        public bool IsPickerMode
        {
            get;
        }

        public AvaloniaList<Models.SSHKeyPair> Keys
        {
            get;
        }

        public Models.SSHKeyPair SelectedKey
        {
            get => _selectedKey;
            set => SetProperty(ref _selectedKey, value);
        }

        public Models.SSHKeyPair CheckedKey
        {
            get => _checkedKey;
            private set => SetProperty(ref _checkedKey, value);
        }

        public SSHKeyGenerator Generator
        {
            get => _generator;
            private set => SetProperty(ref _generator, value);
        }

        public SSHKeyHelper() : this(false, null)
        {
        }

        public SSHKeyHelper(string currentKey) : this(true, currentKey)
        {
        }

        private SSHKeyHelper(bool isPickerMode, string currentKey)
        {
            IsPickerMode = isPickerMode;
            Keys = new AvaloniaList<Models.SSHKeyPair>();

            var sshDir = new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh"));
            if (sshDir.Exists)
            {
                var files = sshDir.GetFiles("*.pub");
                var keys = new List<Models.SSHKeyPair>();

                foreach (var file in files)
                {
                    var privateKeyPath = file.FullName.Substring(0, file.FullName.Length - 4);
                    if (File.Exists(privateKeyPath))
                        keys.Add(new(privateKeyPath, file.FullName));
                }

                if (keys.Count > 0)
                {
                    keys.Sort((l, r) => l.Name.CompareTo(r.Name));
                    Keys.AddRange(keys);
                    SelectedKey = keys[0];
                }
            }

            if (isPickerMode && !string.IsNullOrWhiteSpace(currentKey))
            {
                var current = FindOrAddKey(currentKey.Trim());
                if (current != null)
                {
                    SetKeyChecked(current, true);
                    SelectedKey = current;
                }
            }
        }

        public void SetKeyChecked(Models.SSHKeyPair key, bool isChecked)
        {
            if (isChecked)
            {
                if (_checkedKey != null && _checkedKey != key)
                    _checkedKey.IsChecked = false;

                key.IsChecked = true;
                CheckedKey = key;
            }
            else
            {
                key.IsChecked = false;
                if (_checkedKey == key)
                    CheckedKey = null;
            }
        }

        public void OpenGenerator()
        {
            Generator = new SSHKeyGenerator();
        }

        public void CloseGenerator()
        {
            Generator = null;
        }

        public void Generate()
        {
            var key = _generator.Run();
            if (key == null)
                return;

            Keys.Add(key);
            SelectedKey = key;
            Generator = null;

            if (IsPickerMode)
                SetKeyChecked(key, true);
        }

        public void DeleteSelected()
        {
            var key = SelectedKey;
            if (key == null)
                return;

            try
            {
                if (File.Exists(key.PrivateKeyPath))
                    File.Delete(key.PrivateKeyPath);
                if (File.Exists(key.PublicKeyPath))
                    File.Delete(key.PublicKeyPath);

                if (_checkedKey == key)
                    SetKeyChecked(key, false);

                var idx = Keys.IndexOf(key);
                if (idx >= 0)
                {
                    Keys.RemoveAt(idx);

                    if (Keys.Count == 0)
                        SelectedKey = null;
                    else if (idx > Keys.Count - 1)
                        SelectedKey = Keys[Keys.Count - 1];
                    else
                        SelectedKey = Keys[idx];
                }
            }
            catch
            {
                // Ignore any errors during deletion
            }
        }

        private Models.SSHKeyPair FindOrAddKey(string path)
        {
            string fullPath;
            try
            {
                if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
                    path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));

                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }

            var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            foreach (var key in Keys)
            {
                if (string.Equals(Path.GetFullPath(key.PrivateKeyPath), fullPath, comparison))
                    return key;
            }

            // The key lives outside of `~/.ssh`, but still list it so that it can stay selected.
            if (!File.Exists(fullPath))
                return null;

            var external = new Models.SSHKeyPair(fullPath, fullPath + ".pub");
            Keys.Insert(0, external);
            return external;
        }

        private Models.SSHKeyPair _selectedKey = null;
        private Models.SSHKeyPair _checkedKey = null;
        private SSHKeyGenerator _generator = null;
    }
}
