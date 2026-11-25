using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Queries the submodules that `git submodule update` updates: the ones not on the recorded commit (`+`) and the
    ///     ones that are not cloned (`-`). `git submodule status` shows the inactive submodules as not cloned too, even if
    ///     they are, and `git submodule update` skips those unless it is given `--init`. So without `includeInactive`, only
    ///     the active ones of them are kept, like a newly added submodule that `submodule.active` matches.
    /// </summary>
    public partial class QueryUpdatableSubmodules : Command
    {
        [GeneratedRegex(@"^([\-\+])([0-9a-f]+)\s(.*?)(\s\(.*\))?$")]
        private static partial Regex REG_FORMAT_STATUS();

        public QueryUpdatableSubmodules(string repo, bool includeInactive)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "submodule status";

            _includeInactive = includeInactive;
        }

        public async Task<List<string>> GetResultAsync()
        {
            var submodules = new List<string>();
            var notCloned = new List<string>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT_STATUS().Match(line);
                if (match.Success)
                {
                    var path = match.Groups[3].Value;
                    if (match.Groups[1].Value.StartsWith('-'))
                        notCloned.Add(path);

                    submodules.Add(path);
                }
            }

            if (!_includeInactive && notCloned.Count > 0)
            {
                var active = await new QueryActiveSubmodules(WorkingDirectory, notCloned).GetResultAsync().ConfigureAwait(false);
                submodules.RemoveAll(path => notCloned.Contains(path) && !active.ContainsKey(path));
            }

            return submodules;
        }

        private readonly bool _includeInactive;
    }
}
