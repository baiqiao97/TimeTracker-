# TimeTracker

跨平台时间追踪工具，支持 Windows / Android / 自建服务端。

> 自动记录你的应用使用时长，按标签分类统计，支持活动模式多场景追踪，数据可通过私有服务器跨设备同步。

## 特性

- ⏱ **自动追踪** — 后台记录每个应用/窗口的使用时长
- 🏷 **标签分类** — 为应用分配标签（工作/学习/娱乐/社交）
- 📊 **可视化统计** — 柱状图/饼图/折线图展示每日/每周/每月趋势
- 🎯 **活动模式** — 同一应用在不同场景下独立追踪
- ☁️ **私有同步** — 自建服务端，数据不经过第三方
- 🔒 **安全认证** — PBKDF2 密码哈希、Token 过期、本地嵌入式服务器
- 🎨 **现代 UI** — 圆角卡片、流畅动画、暗色主题

## 结构

```
TimeTracker/
├── Windows/          # WPF 桌面端 (.NET 8)
│   ├── TrackingService.cs   # 窗口追踪引擎
│   ├── EmbeddedServer.cs    # 嵌入式同步服务器
│   └── ServerSyncClient.cs  # 同步客户端
├── Android/          # Android 移动端
│   ├── TrackingService.java # 应用使用时长追踪
│   └── MainActivity.java    # 主界面 + 活动管理
└── Server/           # 独立服务端 (.NET)
    └── Program.cs           # REST API (注册/登录/同步)
```

## 快速开始

### Windows 桌面端

```bash
cd Windows
dotnet run
```

### Android 端

用 Android Studio 打开 `Android/` 目录，直接运行。

### 自建服务端

```bash
cd Server
dotnet run
```

服务端默认监听 `http://localhost:5080`。在设置中配置服务端地址即可启用跨设备同步。

## 部署服务端

### 方式一：Docker（推荐）

```bash
cd Server
docker build -t timetracker-server .
docker run -d -p 5080:5080 --restart always --name timetracker timetracker-server
```

### 方式二：Linux VPS 手动部署

```bash
# 安装 .NET Runtime
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0 --runtime aspnetcore

# 上传发布包
scp -r publish/ user@your-server:/opt/timetracker/

# 启动服务端
cd /opt/timetracker
nohup dotnet TimeTrackerServer.dll --urls "http://0.0.0.0:5080" > server.log 2>&1 &
```

### 方式三：发布为可执行文件

```bash
cd Server

# Windows
dotnet publish -c Release -r win-x64 --self-contained -o publish/win

# Linux
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux

# 运行
./publish/linux/TimeTrackerServer
```

### 配置 systemd 服务（Linux）

```ini
# /etc/systemd/system/timetracker.service
[Unit]
Description=TimeTracker Server
After=network.target

[Service]
WorkingDirectory=/opt/timetracker
ExecStart=/opt/timetracker/TimeTrackerServer --urls "http://0.0.0.0:5080"
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable timetracker --now
```

### 配置 Nginx 反向代理（可选 HTTPS）

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
```

服务端部署后，在客户端设置中填写 `http://你的服务器IP:5080` 即可连接。

## 同步模式

| 模式 | 说明 |
|------|------|
| 标准模式 | 单机使用，无需网络 |
| 活动模式 | 同一应用按活动分类追踪 |
| 账号登录 | 注册账号接入私有服务端 |
| 自建服务器 | 本机作为服务端供其他设备连接 |

## 技术栈

- **Windows**: WPF + System.Data.SQLite + HttpListener
- **Android**: Java + Room Database + AccessibilityService
- **Server**: ASP.NET Core Minimal API + Microsoft.Data.Sqlite
- **安全**: PBKDF2 SHA-256 (100k 迭代) + Token 认证

## 许可

MIT License — 详见 [LICENSE](LICENSE)
