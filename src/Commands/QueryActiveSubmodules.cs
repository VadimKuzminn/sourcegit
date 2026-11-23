using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Finds out which of the given submodules are active, the way Git does it: `submodule.&lt;name&gt;.active` if it is
    ///     set, otherwise whether the path matches the pathspec in `submodule.active` if that is set (`git clone
    ///     --recurse-submodules` sets it), otherwise whether `submodule.&lt;name&gt;.url` is set. `git submodule update`
    ///     without `--init` updates only the active submodules, and clones the ones that are not cloned yet.
    ///     Returns the names of the active ones by path.
    /// </summary>
    public class QueryActiveSubmodules : Command
    {
        public QueryActiveSubmodules(string repo, List<string> paths)
        {
            WorkingDirectory = repo;
            Context = repo;

            _paths = paths;
        }

        public async Task<Dictionary<string, string>> GetResultAsync()
        {
            var active = new Dictionary<string, string>();
            if (_paths.Count == 0)
                return active;

            // A path that has no name in `.gitmodules` is never active.
            var names = new Dictionary<string, string>();
            Args = @"config -z --file .gitmodules --get-regexp ^submodule\..+\.path$";
            foreach (var (key, value) in await ReadEntriesAsync().ConfigureAwait(false))
            {
                if (value != null && TryGetName(key, ".path", out var name))
                    names[value] = name;
            }

            var flags = new Dictionary<string, bool>();
            var urls = new HashSet<string>();
            var pathspecs = new List<string>();
            Args = @"config -z --get-regexp ^submodule\.";
            foreach (var (key, value) in await ReadEntriesAsync().ConfigureAwait(false))
            {
                if (key.Equals("submodule.active", StringComparison.Ordinal))
                {
                    if (!string.IsNullOrEmpty(value))
                        pathspecs.Add(value);
                }
                else if (TryGetName(key, ".active", out var name))
                {
                    flags[name] = ParseBool(value);
                }
                else if (value != null && TryGetName(key, ".url", out name))
                {
                    urls.Add(name);
                }
            }

            var unflagged = new List<string>();
            foreach (var path in _paths)
            {
                if (!names.TryGetValue(path, out var name))
                    continue;

                if (flags.TryGetValue(name, out var flag))
                {
                    if (flag)
                        active[path] = name;
                }
                else if (pathspecs.Count > 0)
                {
                    unflagged.Add(path);
                }
                else if (urls.Contains(name))
                {
                    active[path] = name;
                }
            }

            if (unflagged.Count > 0)
            {
                var matched = await MatchPathspecsAsync(pathspecs, unflagged).ConfigureAwait(false);
                foreach (var path in matched)
                    active[path] = names[path];
            }

            return active;
        }

        /// <summary>
        ///     Returns the paths that the pathspecs match. `git ls-files` applies the pathspec rules (wildcards, magic) the
        ///     same way as Git does for `submodule.active`, but it lists every file they match, so it is read line by line.
        /// </summary>
        private async Task<HashSet<string>> MatchPathspecsAsync(List<string> pathspecs, List<string> paths)
        {
            var matched = new HashSet<string>();

            // `git clone --recurse-submodules` writes `.`, which matches everything.
            if (pathspecs.TrueForAll(s => s.Equals(".", StringComparison.Ordinal)))
            {
                matched.UnionWith(paths);
                return matched;
            }

            var builder = new StringBuilder();
            builder.Append("ls-files --");
            foreach (var pathspec in pathspecs)
                builder.Append(' ').Append(pathspec.Quoted());

            Args = builder.ToString();

            var wanted = new HashSet<string>(paths);
            try
            {
                using var proc = new Process();
                proc.StartInfo = CreateGitStartInfo(true);
                proc.Start();

                while (await proc.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (wanted.Contains(line))
                        matched.Add(line);
                }

                await proc.WaitForExitAsync().ConfigureAwait(false);
            }
            catch
            {
                // Leave the submodules out, as if they were not active.
            }

            return matched;
        }

        /// <summary>
        ///     Reads the `key\nvalue\0` entries of `git config -z`. A key without a value (`key\0`) has a null value.
        /// </summary>
        private async Task<List<(string, string)>> ReadEntriesAsync()
        {
            var entries = new List<(string, string)>();

            // `--get-regexp` fails when nothing matches, but its output is empty then anyway.
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            foreach (var entry in rs.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = entry.IndexOf('\n');
                entries.Add(idx < 0 ? (entry, null) : (entry[..idx], entry[(idx + 1)..]));
            }

            return entries;
        }

        private static bool TryGetName(string key, string suffix, out string name)
        {
            const string prefix = "submodule.";
            if (key.Length > prefix.Length + suffix.Length &&
                key.StartsWith(prefix, StringComparison.Ordinal) &&
                key.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length);
                return true;
            }

            name = null;
            return false;
        }

        /// <summary>
        ///     Same as `git_config_bool`: a key without a value is true, an empty value is false.
        /// </summary>
        private static bool ParseBool(string value)
        {
            if (value == null)
                return true;

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;

            return long.TryParse(value, out var number) && number != 0;
        }

        private readonly List<string> _paths;
    }
}
