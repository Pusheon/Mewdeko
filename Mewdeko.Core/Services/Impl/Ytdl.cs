using System.Diagnostics;
using System.Threading.Tasks;
using NLog;

namespace Mewdeko.Core.Services.Impl
{
    public class YtdlOperation
    {
        private readonly Logger _log;

        public YtdlOperation()
        {
            _log = LogManager.GetCurrentClassLogger();
        }

        public async Task<string> GetDataAsync(string url)
        {
            // escape the minus on the video argument
            // to prevent youtube-dl to handle it like an argument
            if (url != null && url.StartsWith("-"))
                url = '\\' + url;

            using (var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "youtube-dl",
                    Arguments =
                        $"-4 --geo-bypass -f bestaudio -e --get-url --get-id --get-thumbnail --verbose --get-duration --cookies /home/rootish/cookies.txt --no-check-certificate --default-search \"ytsearch:\" \"{url}\"",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            })
            {
                _log.Debug($"Executing {process.StartInfo.FileName} {process.StartInfo.Arguments}");

                process.Start();
                var str = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
                var err = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(err))
                    _log.Warn(err);
                return str;
            }
        }
    }
}