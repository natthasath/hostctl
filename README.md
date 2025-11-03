# 🎉 Hostctl
hostctl is a fast CLI to manage your hosts file with isolated profiles, toggles, and comments. Enable/disable groups, switch environments, import/export entries, and apply changes across Linux, macOS, and Windows. Supports templates, backups & automation.

![version](https://img.shields.io/badge/version-1.0-blue)
![rating](https://img.shields.io/badge/rating-★★★★★-yellow)
![uptime](https://img.shields.io/badge/uptime-100%25-brightgreen)

### ❓ Hostctl Help
```shell
hostctl - Windows hosts manager with tags

Usage:
  hostctl list [--tag <name>] [--all] [--sort ip|name|tag] [--desc]
  hostctl add --ip <ip> --host <hostname[,hostname2]> [--tag tag1,tag2] [--comment "text"]
  hostctl edit --host <old> [--ip <newip>] [--rename <newhost>] [--tag "+web,-old"]
  hostctl remove --host <hostname>
  hostctl tags
  hostctl backup
  hostctl --help | -h
  hostctl --version | -V | -v

Notes:
  • Commands that modify the hosts file require Administrator privileges.
  • Tags metadata: C:\ProgramData\hostctl\hosts.tags.json
  • Hosts file    : C:\WINDOWS\system32\drivers\etc\hosts
```

### 🏆 RUN

```shell
dotnet clean
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
```