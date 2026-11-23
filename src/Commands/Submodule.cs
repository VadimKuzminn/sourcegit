using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace SourceGit.Commands
{
    public class Submodule : Command
    {
        public Submodule(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
        }

        public async Task<bool> AddAsync(string url, string relativePath, bool recursive)
        {
            Args = $"-c protocol.file.allow=always submodule add {url.Quoted()} {relativePath.Quoted()}";

            var succ = await ExecAsync().ConfigureAwait(false);
            if (!succ)
                return false;

            if (recursive)
                Args = $"submodule update --init --recursive -- {relativePath.Quoted()}";
            else
                Args = $"submodule update --init -- {relativePath.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> SetURLAsync(string path, string url)
        {
            Args = $"submodule set-url -- {path.Quoted()} {url.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> SetBranchAsync(string path, string branch)
        {
            if (string.IsNullOrEmpty(branch))
                Args = $"submodule set-branch -d -- {path.Quoted()}";
            else
                Args = $"submodule set-branch -b {branch.Quoted()} -- {path.Quoted()}";

            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> UpdateAsync(List<string> modules, bool init, bool recursive, bool useRemote)
        {
            await PrepareUpdateAsync(WorkingDirectory, string.Empty, modules, recursive, useRemote).ConfigureAwait(false);

            var builder = new StringBuilder();
            builder.Append("submodule update --progress");

            if (init)
                builder.Append(" --init");
            if (recursive)
                builder.Append(" --recursive");
            if (useRemote)
                builder.Append(" --remote");
            if (modules.Count > 0)
            {
                builder.Append(" --");
                foreach (var module in modules)
                    builder.Append(' ').Append(module.Quoted());
            }

            Args = builder.ToString();
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> CheckoutRecordedRevisionAsync(string module)
        {
            // `--checkout` ignores the `submodule.<name>.update` setting, which could rebase or merge instead.
            Args = $"submodule update --checkout -- {module.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        public async Task<bool> DeinitAsync(string module, bool force)
        {
            Args = force ? $"submodule deinit -f -- {module.Quoted()}" : $"submodule deinit -- {module.Quoted()}";
            if (!await ExecAsync().ConfigureAwait(false))
                return false;

            // `git submodule deinit` drops the config of the submodule, `submodule.<name>.active` included. If the pathspec
            // in `submodule.active` (set by `git clone --recurse-submodules`) matches it, it is still active then, and the
            // next `git submodule update` clones it again. Turn it off, so that updates skip it until it is initialized
            // again (`git submodule init` turns it back on).
            var active = await new QueryActiveSubmodules(WorkingDirectory, [module]).GetResultAsync().ConfigureAwait(false);
            if (!active.TryGetValue(module, out var name))
                return true;

            return await new Config(WorkingDirectory)
                .Use(Log)
                .SetAsync($"submodule.{name.Quoted()}.active", "false")
                .ConfigureAwait(false);
        }

        public async Task<bool> DeleteAsync(string module)
        {
            Args = $"rm -rf {module.Quoted()}";
            return await ExecAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     `git submodule update` passes `--progress` only to `git clone`: a submodule that is cloned already is fetched
        ///     and checked out quietly, so nothing tells how far the update of a heavy one is. Fetch and check out these
        ///     submodules here with progress first, then `git submodule update` has nothing left to do for them.
        ///     Failures are not reported here, because `git submodule update` runs the same steps again and reports them.
        /// </summary>
        private async Task PrepareUpdateAsync(string repo, string displayPrefix, List<string> modules, bool recursive, bool useRemote)
        {
            var submodules = await new QueryCheckoutSubmodules(repo, modules).GetResultAsync().ConfigureAwait(false);
            foreach (var submodule in submodules)
            {
                if (CancellationToken.IsCancellationRequested)
                    return;

                // With `--remote` it always fetches, otherwise only a submodule that is not checked out yet needs it.
                if (!useRemote && submodule.Status == Models.SubmoduleStatus.Normal)
                    continue;

                var fullPath = Path.GetFullPath(Path.Combine(repo, submodule.Path));
                Log?.AppendLine($"Entering '{displayPrefix}{submodule.Path}'");

                if (useRemote || !await new IsCommitSHA(fullPath, submodule.SHA).GetResultAsync().ConfigureAwait(false))
                {
                    await new Fetch(fullPath) { RaiseError = false }
                        .Use(Log)
                        .WithCancellation(CancellationToken)
                        .ExecAsync()
                        .ConfigureAwait(false);
                }

                // With `--remote` the submodule is updated to its remote branch instead of the recorded commit.
                if (useRemote || CancellationToken.IsCancellationRequested)
                    continue;

                var succ = await new Checkout(fullPath) { RaiseError = false }
                    .Use(Log)
                    .CommitAsync(submodule.SHA, false)
                    .ConfigureAwait(false);

                if (succ && recursive)
                    await PrepareUpdateAsync(fullPath, $"{displayPrefix}{submodule.Path}/", [], true, false).ConfigureAwait(false);
            }
        }
    }
}
