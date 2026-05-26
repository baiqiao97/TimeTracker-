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

```bash
# 克隆仓库
git clone https://github.com/baiqiao97/TimeTracker-.git
cd TimeTracker-
```

### Windows 桌面端

```bash
cd Windows
dotnet run
```

### 自建服务端

```bash
cd Server
dotnet run
```

### Android 端

用 Android Studio 打开 `Android/` 目录，直接运行。

服务端默认监听 `http://localhost:5080`。在设置中配置服务端地址即可启用跨设备同步。

## 部署服务端

### 方式一：Docker（推荐，从零开始）

```bash
# 1. 克隆仓库
git clone https://github.com/baiqiao97/TimeTracker-.git
cd TimeTracker-

# 2. 进入服务端目录构建镜像
cd Server
docker build -t timetracker-server .

# 3. 运行容器（后台运行，开机自启，端口 5080）
docker run -d \
  --name timetracker \
  --restart always \
  -p 5080:5080 \
  -v timetracker-data:/app/data \
  timetracker-server

# 4. 查看日志确认启动成功
docker logs timetracker
```

> 服务端启动后访问 `http://你的服务器IP:5080` 即可。

#### Docker Compose（推荐生产环境）

```yaml
# docker-compose.yml
version: '3.8'
services:
  timetracker:
    build: ./Server
    container_name: timetracker
    restart: always
    ports:
      - "5080:5080"
    volumes:
      - ./data:/app/data
```

```bash
docker compose up -d
```

#### 常用管理命令

```bash
docker logs timetracker         # 查看日志
docker restart timetracker      # 重启服务
docker stop timetracker         # 停止服务
docker rm timetracker           # 删除容器（数据在 volume 中不受影响）
docker exec -it timetracker sh  # 进入容器
```

### 方式二：Linux VPS 手动部署

```bash
# 1. 克隆仓库
git clone https://github.com/baiqiao97/TimeTracker-.git
cd TimeTracker-/Server

# 2. 安装 .NET Runtime（如果未安装）
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 10.0 --runtime aspnetcore
export PATH="$HOME/.dotnet:$PATH"

# 3. 发布服务端
dotnet publish -c Release -r linux-x64 --self-contained -o /opt/timetracker

# 4. 启动
cd /opt/timetracker
nohup ./TimeTrackerServer --urls "http://0.0.0.0:5080" > server.log 2>&1 &

# 5. 验证
curl http://localhost:5080
```

### 方式三：Windows 本地运行

```bash
git clone https://github.com/baiqiao97/TimeTracker-.git
cd TimeTracker-/Server
dotnet run
```

### 配置 systemd 开机自启（Linux）

创建服务文件：

```bash
sudo nano /etc/systemd/system/timetracker.service
```

写入以下内容：

```ini
[Unit]
Description=TimeTracker Server
After=network.target

[Service]
WorkingDirectory=/opt/timetracker
ExecStart=/opt/timetracker/TimeTrackerServer --urls "http://0.0.0.0:5080"
Restart=always
RestartSec=5
User=www-data
Environment=DOTNET_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

启动服务：

```bash
sudo systemctl daemon-reload
sudo systemctl enable timetracker --now
sudo systemctl status timetracker
```

### 配置 Nginx 反向代理（可选 HTTPS）

```bash
sudo apt install nginx certbot python3-certbot-nginx -y

sudo nano /etc/nginx/sites-available/timetracker
```

```nginx
server {
    listen 80;
    server_name tracker.your-domain.com;

    location / {
        proxy_pass http://127.0.0.1:5080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/timetracker /etc/nginx/sites-enabled/
sudo nginx -t && sudo systemctl reload nginx

# 申请免费 SSL 证书
sudo certbot --nginx -d tracker.your-domain.com
```

### 服务端部署完成后

在 Windows 客户端「设置 → 云同步」中填写服务端地址 `http://你的服务器IP:5080`，注册账号后即可跨设备同步。

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
