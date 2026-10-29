#!/bin/sh
# 容器重建后一键恢复 PM 环境
# 用法（容器内）：sh /root/pm-backup/restore.sh
set -e
export DEBIAN_FRONTEND=noninteractive

echo "[1/5] 换 apt 源为阿里云 HTTP"
sed -i "s|http://deb.debian.org|http://mirrors.aliyun.com|g; s|https://mirrors.aliyun.com|http://mirrors.aliyun.com|g" \
  /etc/apt/sources.list.d/debian.sources 2>/dev/null || true
apt-get update -qq

echo "[2/5] 安装 curl / wget / unzip / libicu"
apt-get install -y --no-install-recommends curl wget unzip libicu72 libssl3 2>&1 | tail -2

echo "[3/5] 安装 .NET 10 SDK"
mkdir -p /usr/local/dotnet
cd /tmp
[ -f sdk.tar.gz ] || curl -sSL -o sdk.tar.gz https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz
[ -f aspnet.tar.gz ] || curl -sSL -o aspnet.tar.gz https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.12/aspnetcore-runtime-10.0.12-linux-x64.tar.gz
tar xzf sdk.tar.gz -C /usr/local/dotnet
tar xzf aspnet.tar.gz -C /usr/local/dotnet
ln -sf /usr/local/dotnet/dotnet /usr/local/bin/dotnet
cat > /etc/profile.d/dotnet.sh <<'EOS'
export DOTNET_ROOT=/usr/local/dotnet
export PATH=/usr/local/dotnet:$PATH
EOS
chmod +x /etc/profile.d/dotnet.sh
export DOTNET_ROOT=/usr/local/dotnet
export PATH=/usr/local/dotnet:$PATH
dotnet --version

echo "[4/5] 发布 PM"
cd /root/saibo/apps/pm/api
dotnet publish Sb.Apps.Pm.WebApi/Sb.Apps.Pm.WebApi.csproj -c Release -o /opt/pm/api --nologo -v quiet

echo "[5/5] 恢复配置与启动脚本"
mkdir -p /opt/pm
cp /root/pm-backup/appsettings.json /opt/pm/api/appsettings.json
cp /root/pm-backup/installed.lock /opt/pm/api/installed.lock 2>/dev/null || true
cp /root/pm-backup/start.sh /opt/pm/start.sh
chmod +x /opt/pm/start.sh
/opt/pm/start.sh 6200

echo "完成。PM 已启动。"
