using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Queries the cloned submodules that `git submodule update` updates by checking out the commit recorded in the
    ///     repository (the default update mode). The SHA of each one is that recorded commit, and its status tells whether
    ///     it is checked out already (`Normal`) or not (`RevisionChanged`).
    /// </summary>
    public partial class QueryCheckoutSubmodules : Command
    {
        [GeneratedRegex(@"^([U\-\+ ])([0-9a-f]+)\s(.*?)(\s\(.*\))?$")]
        private static partial Regex REG_FORMAT_STATUS();
        [GeneratedRegex(@"^submodule\.([^=]+)\.(path|update)=(.*)$")]
        private static partial Regex REG_FORMAT_MODULE_INFO();

        public QueryCheckoutSubmodules(string repo, List<string> modules)
        {
            WorkingDirectory = repo;
            Context = repo;

            _modules = modules;
        }

        public async Task<List<Models.Submodule>> GetResultAsync()
        {
            var builder = new StringBuilder();
            builder.Append("submodule status --cached");
            if (_modules.Count > 0)
            {
                builder.Append(" --");
                foreach (var module in _modules)
                    builder.Append(' ').Append(module.Quoted());
            }

            Args = builder.ToString();

            var submodules = new List<Models.Submodule>();
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return submodules;

            var lines = rs.StdOut.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT_STATUS().Match(line);
                if (!match.Success)
                    continue;

                // Skip the submodules that are not cloned (`-`) or have conflicts (`U`).
                var status = match.Groups[1].Value[0] switch
                {
                    ' ' => Models.SubmoduleStatus.Normal,
                    '+' => Models.SubmoduleStatus.RevisionChanged,
                    _ => Models.SubmoduleStatus.NotInited,
                };

                if (status != Models.SubmoduleStatus.NotInited)
                    submodules.Add(new Models.Submodule() { Path = match.Groups[3].Value, SHA = match.Groups[2].Value, Status = status });
            }

            if (submodules.Count > 0)
            {
                var modes = await QueryUpdateModesAsync().ConfigureAwait(false);
                submodules.RemoveAll(s => modes.TryGetValue(s.Path, out var mode) && !mode.Equals("checkout", StringComparison.Ordinal));
            }

            return submodules;
        }

        /// <summary>
        ///     Returns the update mode of the submodules by path. The one in the config of the repository wins over the one
        ///     in `.gitmodules`, like in `git submodule update`.
        /// </summary>
        private async Task<Dictionary<string, string>> QueryUpdateModesAsync()
        {
            var paths = new Dictionary<string, string>();
            var modes = new Dictionary<string, string>();

            Args = "config --file .gitmodules --list";
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (rs.IsSuccess)
                ParseModuleInfo(rs.StdOut, paths, modes);

            Args = "config --list";
            rs = await ReadToEndAsync().ConfigureAwait(false);
            if (rs.IsSuccess)
                ParseModuleInfo(rs.StdOut, null, modes);

            var result = new Dictionary<string, string>();
            foreach (var (name, mode) in modes)
                result[paths.GetValueOrDefault(name, name)] = mode;

            return result;
        }

        private static void ParseModuleInfo(string output, Dictionary<string, string> paths, Dictionary<string, string> modes)
        {
            var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var match = REG_FORMAT_MODULE_INFO().Match(line);
                if (!match.Success)
                    continue;

                var name = match.Groups[1].Value;
                var value = match.Groups[3].Value;
                if (match.Groups[2].Value.Equals("update", StringComparison.Ordinal))
                    modes[name] = value;
                else if (paths != null)
                    paths[name] = value;
            }
        }

        private readonly List<string> _modules;
    }
}
