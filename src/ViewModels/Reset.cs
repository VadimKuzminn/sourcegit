using System;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class Reset : Popup
    {
        public Models.Branch Current
        {
            get;
        }

        public Models.Commit To
        {
            get;
        }

        public Models.ResetMode SelectedMode
        {
            get;
            set;
        }

        public Reset(Repository repo, Models.Branch current, Models.Commit to)
        {
            _repo = repo;
            Current = current;
            To = to;

            // `--hard` is never remembered (see Sure), so a stored index can never preselect it.
            var modes = Models.ResetMode.Supported;
            var idx = repo.UIStates.LastResetMode;
            SelectedMode = idx >= 0 && idx < modes.Length && modes[idx].Arg != "--hard" ? modes[idx] : modes[1];
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();

            // Remember the picked mode for the next reset, except `--hard`: preselecting it would make
            // an accidental confirm throw away uncommitted work.
            var idx = Array.IndexOf(Models.ResetMode.Supported, SelectedMode);
            if (idx >= 0 && SelectedMode.Arg != "--hard")
                _repo.UIStates.LastResetMode = idx;

            ProgressDescription = $"Reset current branch to {To.SHA} ...";

            var log = _repo.CreateLog($"Reset HEAD to '{To.SHA}'");
            Use(log);

            var succ = await new Commands.Reset(_repo.FullPath, To.SHA, SelectedMode.Arg)
                .Use(log)
                .ExecAsync();

            await _repo.AutoUpdateSubmodulesAsync(log);

            log.Complete();
            return succ;
        }

        private readonly Repository _repo = null;
    }
}
