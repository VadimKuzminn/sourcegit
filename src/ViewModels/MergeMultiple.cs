using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SourceGit.ViewModels
{
    public class MergeMultiple : Popup
    {
        public List<object> Targets
        {
            get;
        } = [];

        public bool AutoCommit
        {
            get;
            set;
        }

        public Models.MergeStrategy Strategy
        {
            get;
            set;
        }

        public MergeMultiple(Repository repo, List<Models.Commit> commits)
        {
            _repo = repo;
            Targets.AddRange(commits);
            AutoCommit = true;
            Strategy = GetLastStrategy();
        }

        public MergeMultiple(Repository repo, List<Models.Branch> branches)
        {
            _repo = repo;
            Targets.AddRange(branches);
            AutoCommit = true;
            Strategy = GetLastStrategy();
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            _repo.ClearCommitMessage();
            _repo.UIStates.LastMergeMultipleStrategy = Math.Max(0, Models.MergeStrategy.ForMultiple.IndexOf(Strategy));
            ProgressDescription = "Merge head(s) ...";

            var log = _repo.CreateLog("Merge Multiple Heads");
            Use(log);

            await new Commands.Merge(
                _repo.FullPath,
                ConvertTargetToMergeSources(),
                AutoCommit,
                Strategy.Arg)
                .Use(log)
                .ExecAsync();

            log.Complete();
            return true;
        }

        private List<string> ConvertTargetToMergeSources()
        {
            var ret = new List<string>();
            foreach (var t in Targets)
            {
                if (t is Models.Branch branch)
                {
                    ret.Add(branch.FriendlyName);
                }
                else if (t is Models.Commit commit)
                {
                    var d = commit.Decorators.Find(x => x.Type is
                        Models.DecoratorType.LocalBranchHead or
                        Models.DecoratorType.RemoteBranchHead or
                        Models.DecoratorType.Tag);

                    if (d != null)
                        ret.Add(d.Name);
                    else
                        ret.Add(commit.SHA);
                }
            }

            return ret;
        }

        private Models.MergeStrategy GetLastStrategy()
        {
            var strategies = Models.MergeStrategy.ForMultiple;
            var idx = _repo.UIStates.LastMergeMultipleStrategy;
            return idx >= 0 && idx < strategies.Count ? strategies[idx] : strategies[0];
        }

        private readonly Repository _repo = null;
    }
}
