using System;
using System.Collections.Generic;

using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class HotkeyEditorItem : ObservableObject
    {
        public Models.HotkeyDefinition Definition { get; }
        public string Label { get; }

        public string GestureText
        {
            get => _gestureText;
            private set => SetProperty(ref _gestureText, value);
        }

        public bool HasGesture
        {
            get => _hasGesture;
            private set => SetProperty(ref _hasGesture, value);
        }

        public bool IsCustomized
        {
            get => _isCustomized;
            private set => SetProperty(ref _isCustomized, value);
        }

        public bool IsRecording
        {
            get => _isRecording;
            set => SetProperty(ref _isRecording, value);
        }

        public HotkeyEditorItem(Models.HotkeyDefinition def)
        {
            Definition = def;
            Label = App.Text(def.LabelKey).Trim().TrimEnd('.', '…');
            Refresh();
        }

        public void Refresh()
        {
            var hotkeys = HotkeyManager.Instance;
            GestureText = hotkeys.GetText(Definition.Id);
            HasGesture = !string.IsNullOrEmpty(GestureText);
            IsCustomized = hotkeys.IsCustomized(Definition.Id);
        }

        private string _gestureText = string.Empty;
        private bool _hasGesture = false;
        private bool _isCustomized = false;
        private bool _isRecording = false;
    }

    public class HotkeyEditorGroup
    {
        public string Header { get; }
        public List<HotkeyEditorItem> Items { get; }

        public HotkeyEditorGroup(string header, List<HotkeyEditorItem> items)
        {
            Header = header;
            Items = items;
        }
    }

    public class HotkeyEditor : ObservableObject
    {
        public List<HotkeyEditorGroup> Groups
        {
            get => _groups;
            private set => SetProperty(ref _groups, value);
        }

        public string Filter
        {
            get => _filter;
            set
            {
                if (SetProperty(ref _filter, value))
                    UpdateGroups();
            }
        }

        public HotkeyEditorItem Recording
        {
            get => _recording;
            private set => SetProperty(ref _recording, value);
        }

        public string Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        public bool IsMessageWarning
        {
            get => _isMessageWarning;
            private set => SetProperty(ref _isMessageWarning, value);
        }

        public bool HasCustomized
        {
            get => _hasCustomized;
            private set => SetProperty(ref _hasCustomized, value);
        }

        /// <summary>
        ///     True while a shortcut that another command uses waits for the user to confirm the reassignment.
        /// </summary>
        public bool IsConfirmingReassign
        {
            get => _isConfirmingReassign;
            private set => SetProperty(ref _isConfirmingReassign, value);
        }

        public HotkeyEditor()
        {
            foreach (var def in HotkeyManager.Instance.Definitions)
                _items.Add(new HotkeyEditorItem(def));

            HasCustomized = HotkeyManager.Instance.HasCustomized();
            UpdateGroups();
        }

        public void ClearFilter()
        {
            Filter = string.Empty;
        }

        /// <summary>
        ///     Shows only the commands that use this exact shortcut (null shows all).
        /// </summary>
        public KeyGesture FilterGesture
        {
            get => _filterGesture;
            private set
            {
                if (SetProperty(ref _filterGesture, value))
                {
                    FilterGestureText = Models.HotkeyGesture.Format(value);
                    UpdateGroups();
                }
            }
        }

        public string FilterGestureText
        {
            get => _filterGestureText;
            private set => SetProperty(ref _filterGestureText, value);
        }

        public bool IsRecordingFilter
        {
            get => _isRecordingFilter;
            private set => SetProperty(ref _isRecordingFilter, value);
        }

        /// <summary>
        ///     Text shown instead of the list when no command passes the filters.
        /// </summary>
        public string EmptyMessage
        {
            get => _emptyMessage;
            private set => SetProperty(ref _emptyMessage, value);
        }

        public bool IsCapturingKeys => _recording != null || _isRecordingFilter;

        public void StartRecordingFilter()
        {
            CancelRecording();
            CancelReassign();

            IsRecordingFilter = true;
            ShowMessage(App.Text("Hotkeys.Editor.FindByShortcutTip"), false);
        }

        public void CancelRecordingFilter()
        {
            if (!_isRecordingFilter)
                return;

            IsRecordingFilter = false;
            ShowMessage(string.Empty, false);
        }

        public void ClearFilterGesture()
        {
            CancelRecordingFilter();
            FilterGesture = null;
        }

        public void StartRecording(HotkeyEditorItem item)
        {
            CancelRecording();
            CancelRecordingFilter();
            CancelReassign();

            item.IsRecording = true;
            Recording = item;
            ShowMessage(App.Text("Hotkeys.Editor.RecordingTip"), false);
        }

        public void CancelRecording()
        {
            if (_recording == null)
                return;

            _recording.IsRecording = false;
            Recording = null;
            ShowMessage(string.Empty, false);
        }

        /// <summary>
        ///     Feeds a key press to the item (or the shortcut filter) being recorded. Returns true when the key
        ///     was consumed.
        /// </summary>
        public bool OnKeyPressed(Key key, KeyModifiers modifiers)
        {
            if (!IsCapturingKeys)
                return false;

            if (Models.HotkeyGesture.IsModifierKey(key))
                return true;

            if (key == Key.Escape && modifiers == KeyModifiers.None)
            {
                CancelRecording();
                CancelRecordingFilter();
                return true;
            }

            var gesture = Models.HotkeyGesture.Create(key, modifiers);
            if (_isRecordingFilter)
            {
                IsRecordingFilter = false;
                ShowMessage(string.Empty, false);
                FilterGesture = gesture;
                return true;
            }

            if (!Models.HotkeyGesture.IsAssignable(gesture))
            {
                ShowMessage(App.Text("Hotkeys.Editor.NeedModifier", Models.HotkeyGesture.Format(gesture)), true);
                return true;
            }

            var item = _recording;
            _recording.IsRecording = false;
            Recording = null;

            AssignOrAsk(item, gesture);
            return true;
        }

        /// <summary>
        ///     Takes the shortcut waiting for confirmation away from the commands that use it and gives it to the
        ///     command the user was editing.
        /// </summary>
        public void ConfirmReassign()
        {
            if (_pending == null)
                return;

            var pending = _pending;
            DismissPending();

            HotkeyManager.Instance.Assign(pending.Item.Definition.Id, pending.Gesture, pending.Conflicts);
            RefreshAll();
            ShowAssignedMessage(pending.Gesture);
        }

        public void CancelReassign()
        {
            if (_pending == null)
                return;

            DismissPending();
            ShowMessage(string.Empty, false);
        }

        public void Clear(HotkeyEditorItem item)
        {
            CancelRecording();
            CancelReassign();
            HotkeyManager.Instance.Set(item.Definition.Id, null);
            RefreshAll();
        }

        public void Reset(HotkeyEditorItem item)
        {
            CancelRecording();
            CancelReassign();

            var def = item.Definition;
            if (def.Default == null)
            {
                HotkeyManager.Instance.Set(def.Id, null);
                RefreshAll();
            }
            else
            {
                AssignOrAsk(item, def.Default);
            }
        }

        public void ResetAll()
        {
            CancelRecording();
            CancelReassign();
            HotkeyManager.Instance.ResetAll();
            RefreshAll();
        }

        /// <summary>
        ///     Assigns a free shortcut right away. A shortcut that other commands use waits for `ConfirmReassign`.
        /// </summary>
        private void AssignOrAsk(HotkeyEditorItem item, KeyGesture gesture)
        {
            var hotkeys = HotkeyManager.Instance;
            var conflicts = hotkeys.FindConflicts(item.Definition.Id, gesture);
            if (conflicts.Count == 0)
            {
                hotkeys.Set(item.Definition.Id, gesture);
                RefreshAll();
                ShowAssignedMessage(gesture);
                return;
            }

            var names = new List<string>();
            foreach (var c in conflicts)
                names.Add($"«{FindItem(c.Id)?.Label ?? c.Id}»");

            _pending = new PendingReassign(item, gesture, conflicts);
            IsConfirmingReassign = true;
            ShowMessage(App.Text("Hotkeys.Editor.ConflictAsk", Models.HotkeyGesture.Format(gesture), string.Join(", ", names), $"«{item.Label}»"), true);
        }

        private void ShowAssignedMessage(KeyGesture gesture)
        {
            if (IsReserved(gesture))
                ShowMessage(App.Text("Hotkeys.Editor.Reserved", Models.HotkeyGesture.Format(gesture)), true);
            else
                ShowMessage(string.Empty, false);
        }

        private void DismissPending()
        {
            _pending = null;
            IsConfirmingReassign = false;
        }

        private void RefreshAll()
        {
            foreach (var item in _items)
                item.Refresh();

            HasCustomized = HotkeyManager.Instance.HasCustomized();

            // Shortcuts are searchable too, so a change may move items in or out of the filtered list.
            if (!string.IsNullOrWhiteSpace(_filter) || _filterGesture != null)
                UpdateGroups();
        }

        private void UpdateGroups()
        {
            var hotkeys = HotkeyManager.Instance;
            var filter = _filter?.Trim();
            var groups = new List<HotkeyEditorGroup>();
            foreach (var group in Enum.GetValues<Models.HotkeyGroup>())
            {
                var items = new List<HotkeyEditorItem>();
                foreach (var item in _items)
                {
                    if (item.Definition.Group != group)
                        continue;

                    if (!string.IsNullOrEmpty(filter) &&
                        !item.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !item.GestureText.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (_filterGesture != null &&
                        !Models.HotkeyGesture.AreEqual(hotkeys.Get(item.Definition.Id), _filterGesture))
                        continue;

                    items.Add(item);
                }

                if (items.Count == 0)
                    continue;

                var header = group switch
                {
                    Models.HotkeyGroup.Global => "Hotkeys.Global",
                    Models.HotkeyGroup.Repository => "Hotkeys.Repo",
                    _ => $"Hotkeys.{group}",
                };

                groups.Add(new HotkeyEditorGroup(App.Text(header), items));
            }

            Groups = groups;

            if (groups.Count > 0)
                EmptyMessage = string.Empty;
            else if (_filterGesture != null && string.IsNullOrEmpty(filter))
                EmptyMessage = App.Text("Hotkeys.Editor.ShortcutIsFree", _filterGestureText);
            else
                EmptyMessage = App.Text("Hotkeys.Editor.NothingFound");
        }

        private HotkeyEditorItem FindItem(string id)
        {
            return _items.Find(x => x.Definition.Id == id);
        }

        private void ShowMessage(string message, bool isWarning)
        {
            Message = message;
            IsMessageWarning = isWarning;
        }

        /// <summary>
        ///     Gestures that lists and text boxes use for themselves: they win while such a control has focus.
        /// </summary>
        private static bool IsReserved(KeyGesture gesture)
        {
            var cmd = Models.HotkeyGesture.Cmd;
            if (gesture.KeyModifiers == cmd)
                return gesture.Key is Key.A or Key.C or Key.V or Key.X or Key.Y or Key.Z or Key.Left or Key.Right or Key.Home or Key.End or Key.Back or Key.Delete;

            if (gesture.KeyModifiers == (cmd | KeyModifiers.Shift))
                return gesture.Key is Key.C or Key.Z or Key.Left or Key.Right or Key.Home or Key.End;

            return false;
        }

        private record PendingReassign(HotkeyEditorItem Item, KeyGesture Gesture, List<Models.HotkeyDefinition> Conflicts);

        private readonly List<HotkeyEditorItem> _items = new();
        private PendingReassign _pending = null;
        private bool _isConfirmingReassign = false;
        private List<HotkeyEditorGroup> _groups = new();
        private string _filter = string.Empty;
        private KeyGesture _filterGesture = null;
        private string _filterGestureText = string.Empty;
        private bool _isRecordingFilter = false;
        private string _emptyMessage = string.Empty;
        private HotkeyEditorItem _recording = null;
        private string _message = string.Empty;
        private bool _isMessageWarning = false;
        private bool _hasCustomized = false;
    }
}
