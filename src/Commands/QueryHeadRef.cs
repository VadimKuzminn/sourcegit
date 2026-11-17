using System.Threading.Tasks;

namespace SourceGit.Commands
{
    /// <summary>
    ///     Returns the full ref name that HEAD points to (e.g. `refs/heads/main`), or an empty string when HEAD is detached.
    /// </summary>
    public class QueryHeadRef : Command
    {
        public QueryHeadRef(string repo)
        {
            WorkingDirectory = repo;
            Context = repo;
            Args = "symbolic-ref -q HEAD";
            RaiseError = false;
        }

        public async Task<string> GetResultAsync()
        {
            var rs = await ReadToEndAsync().ConfigureAwait(false);
            if (!rs.IsSuccess)
                return string.Empty;

            return rs.StdOut.Trim();
        }
    }
}
