using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace TouchpadRightClick.Utils
{
    /// <summary>
    /// 對 GitHub Releases 比對版本。版本單一來源是 csproj 的 &lt;Version&gt;（→ AssemblyVersion）,
    /// Release 的 tag 由 .github/workflows/release.yml 保證跟它同號（vX.YY）。
    /// </summary>
    public static class UpdateChecker
    {
        public const string Repo = "nec789tw/touchpad-tap-right";
        public static readonly string ReleasesPageUrl = $"https://github.com/{Repo}/releases/latest";
        private static readonly string ApiUrl = $"https://api.github.com/repos/{Repo}/releases/latest";

        /// <summary>目前版本 Major.Minor.Build（csproj 寫 8.70 → 8.70.0；寫 8.70.1 → 8.70.1）</summary>
        public static Version Current
        {
            get
            {
                var v = typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0);
                return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
            }
        }

        /// <summary>顯示用字串,與 tag 同形:"v8.70"，有 patch 才 "v8.70.1"</summary>
        public static string CurrentTag => Current.Build > 0
            ? $"v{Current.Major}.{Current.Minor}.{Current.Build}"
            : $"v{Current.Major}.{Current.Minor}";

        public readonly struct Result
        {
            public Result(string latestTag, bool hasNewer) { LatestTag = latestTag; HasNewer = hasNewer; }
            public string LatestTag { get; }
            public bool HasNewer { get; }
        }

        /// <summary>
        /// 查最新 Release。網路錯誤 / 404 / 解析失敗直接丟例外,由呼叫端顯示訊息。
        /// </summary>
        public static async Task<Result> CheckAsync()
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TouchpadTapRight/" + CurrentTag);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            // ponytail: repo 公開前用 GITHUB_TOKEN 環境變數測試（private repo 的 API 要授權）;公開後不需要,沒設就不帶
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            string json = await http.GetStringAsync(ApiUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            return new Result(tag, IsNewer(tag, Current));
        }

        /// <summary>"v8.71" / "8.71.1" 是否比 current 新。tag 解析不出來一律當「不是新版」。</summary>
        public static bool IsNewer(string tag, Version current)
        {
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return false;
            // 只比 Major.Minor(.Build):Version 未指定的欄位是 -1,先補齊避免 8.70 vs 8.70.0.0 比錯
            var l = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0), 0);
            var c = new Version(current.Major, current.Minor, Math.Max(current.Build, 0), 0);
            return l > c;
        }
    }
}
