# 🎉 Hostctl

Hostctl is a Windows CLI for managing the system hosts file with tag-based organization. It lets you list, add, edit, and remove host entries, group them with tags for filtering, and back up the hosts file before making changes.

![version](https://img.shields.io/github/v/release/natthasath/hostctl)
![platform](https://img.shields.io/badge/platform-windows-lightgrey)
![license](https://img.shields.io/github/license/natthasath/hostctl)

### ✨ Features

- List entries with tag filtering, sorting (`ip`, `name`, `tag`) and ascending/descending order
- Add, edit, and remove hosts entries with multi-hostname and comment support
- Tag entries (`+web,-old` syntax) and list all tags in use
- Back up the hosts file before modifying it
- Ships as a single self-contained `.exe` — no separate .NET runtime install required

### ✅ Requirements

- Windows OS
- Administrator privileges for commands that modify the hosts file (`add`, `edit`, `remove`, `backup`)
- .NET 6 SDK (only needed if building from source)

### 🚀 Installation

```shell
dotnet clean
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:PublishTrimmed=true
```

### 🏆 Usage

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

### 📜 License

This project is licensed under the [MIT License](LICENSE).
