namespace TimeTracker
{
    /// <summary>
    /// 将系统进程名转换为用户友好的显示名称
    /// </summary>
    public static class ProcessNameHelper
    {
        private static readonly Dictionary<string, string> _friendlyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            // 浏览器
            ["chrome"] = "Chrome 浏览器",
            ["firefox"] = "Firefox 浏览器",
            ["msedge"] = "Edge 浏览器",
            ["edge"] = "Edge 浏览器",
            ["iexplore"] = "IE 浏览器",
            ["opera"] = "Opera 浏览器",
            ["brave"] = "Brave 浏览器",
            ["safari"] = "Safari 浏览器",

            // 通讯 / 社交
            ["wechat"] = "微信",
            ["wechatapp"] = "微信",
            ["qq"] = "QQ",
            ["tim"] = "TIM",
            ["dingtalk"] = "钉钉",
            ["feishu"] = "飞书",
            ["lark"] = "飞书",
            ["telegram"] = "Telegram",
            ["whatsapp"] = "WhatsApp",
            ["discord"] = "Discord",
            ["slack"] = "Slack",
            ["teams"] = "Teams",
            ["skype"] = "Skype",
            ["zoom"] = "Zoom",

            // 开发工具
            ["code"] = "VS Code",
            ["devenv"] = "Visual Studio",
            ["visualstudio"] = "Visual Studio",
            ["idea64"] = "IntelliJ IDEA",
            ["idea"] = "IntelliJ IDEA",
            ["pycharm64"] = "PyCharm",
            ["pycharm"] = "PyCharm",
            ["webstorm64"] = "WebStorm",
            ["webstorm"] = "WebStorm",
            ["eclipse"] = "Eclipse",
            ["android studio"] = "Android Studio",
            ["studio64"] = "Android Studio",
            ["rider64"] = "Rider",
            ["rider"] = "Rider",
            ["sublime_text"] = "Sublime Text",
            ["atom"] = "Atom",
            ["nvim-qt"] = "Neovim",
            ["gvim"] = "Vim",

            // 办公软件
            ["winword"] = "Word",
            ["excel"] = "Excel",
            ["powerpnt"] = "PowerPoint",
            ["outlook"] = "Outlook",
            ["wps"] = "WPS Office",
            ["wpspdf"] = "WPS PDF",
            ["notion"] = "Notion",
            ["obsidian"] = "Obsidian",
            ["typora"] = "Typora",
            ["xmind"] = "XMind",
            ["onenote"] = "OneNote",

            // 系统工具
            ["notepad"] = "记事本",
            ["notepad++"] = "Notepad++",
            ["explorer"] = "文件资源管理器",
            ["taskmgr"] = "任务管理器",
            ["cmd"] = "命令提示符",
            ["powershell"] = "PowerShell",
            ["windowsterminal"] = "终端",
            ["wt"] = "终端",
            ["calc"] = "计算器",
            ["snippingtool"] = "截图工具",
            ["mspaint"] = "画图",
            ["regedit"] = "注册表编辑器",
            ["control"] = "控制面板",
            ["onedrive"] = "OneDrive",

            // 设计 / 媒体
            ["photoshop"] = "Photoshop",
            ["illustrator"] = "Illustrator",
            ["afterfx"] = "After Effects",
            ["premiere"] = "Premiere Pro",
            ["figma"] = "Figma",
            ["blender"] = "Blender",
            ["vlc"] = "VLC 播放器",
            ["potplayer"] = "PotPlayer",
            ["obs64"] = "OBS Studio",
            ["obs"] = "OBS Studio",
            ["audacity"] = "Audacity",

            // 游戏 / 娱乐
            ["steam"] = "Steam",
            ["epicgameslauncher"] = "Epic Games",
            ["league of legends"] = "LOL",
            ["valorant"] = "Valorant",
            ["genshinimpact"] = "原神",
            ["bilibili"] = "哔哩哔哩",
            ["neteasecloudmusic"] = "网易云音乐",
            ["qqmusic"] = "QQ音乐",
            ["spotify"] = "Spotify",
            ["youtube"] = "YouTube",
            ["netflix"] = "Netflix",

            // 网盘
            ["baidunetdisk"] = "百度网盘",
            ["alidrive"] = "阿里云盘",
            ["googledrive"] = "Google Drive",
            ["dropbox"] = "Dropbox",

            // 压缩 / 安全
            ["winrar"] = "WinRAR",
            ["7zg"] = "7-Zip",
            ["7zfm"] = "7-Zip",
            ["360sd"] = "360安全卫士",
            ["360tray"] = "360安全卫士",

            // 数据库
            ["mysqld"] = "MySQL",
            ["postgres"] = "PostgreSQL",
            ["mongod"] = "MongoDB",
            ["redis-server"] = "Redis",
            ["redis-cli"] = "Redis CLI",

            // 终端 / SSH
            ["putty"] = "PuTTY",
            ["mobaxterm"] = "MobaXterm",
            ["winterm"] = "WindTerm",
        };

        /// <summary>
        /// 获取进程的友好显示名称；
        /// 如果无映射，返回原始名称的首字母大写形式
        /// </summary>
        public static string GetDisplayName(string? processName)
        {
            if (string.IsNullOrEmpty(processName))
                return "未知应用";

            // 去掉 .exe 后缀再查
            var key = processName;
            if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                key = key[..^4];

            if (_friendlyNames.TryGetValue(key, out var friendly))
                return friendly;

            // 没有映射时，首字母大写返回
            if (key.Length > 0)
                return char.ToUpperInvariant(key[0]) + key[1..];

            return key;
        }

        /// <summary>
        /// 获取原始进程名（用于数据库存储）
        /// </summary>
        public static string GetRawName(string? displayName)
        {
            // 简单反查（主要用于日志等场景）
            foreach (var kv in _friendlyNames)
            {
                if (string.Equals(kv.Value, displayName, StringComparison.OrdinalIgnoreCase))
                    return kv.Key;
            }
            return displayName ?? "unknown";
        }
    }
}
