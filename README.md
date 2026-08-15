# 🎉 Hostctl

Hostctl is a Windows CLI for managing the system hosts file with tag-based organization. It lets you list, add, edit, and remove host entries, group them with tags for filtering, and back up the hosts file before making changes.

![version](https://img.shields.io/github/v/release/natthasath/hostctl)
![platform](https://img.shields.io/badge/platform-windows-lightgrey)
![license](https://img.shields.io/github/license/natthasath/hostctl)

### ✨ Features

- List entries with tag filtering, sorting (`ip`, `name`, `tag`) and ascending/descending order
- `--all` includes disabled (commented-out) entries in the listing, prefixed with `#`
- `--json` on `list`/`tags` for scripting
- Add, edit, and remove hosts entries with multi-hostname and comment support
- `--dry-run` on `add`/`edit`/`remove` previews the change without writing to the hosts file
- `remove` asks for confirmation before deleting (skip with `-y`/`--yes`; required when run non-interactively)
- Tag entries (`+web,-old` syntax) and list all tags in use
- Back up the hosts file before modifying it — every write prints the backup path it created
- Ships as a single self-contained `.exe` — no separate .NET runtime install required

### ✅ Requirements

- Windows OS
- Administrator privileges for commands that write to the hosts file (`add`, `edit`, `remove`, `backup`) — `--dry-run` previews changes without elevation
- .NET 6 SDK (only needed if building from source)

> [!IMPORTANT]
> Run `add`, `edit`, `remove`, and `backup` from an elevated (Administrator) shell — without it, these commands fail when they try to write to the hosts file.

### 🚀 Installation

```shell
dotnet clean
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=true
```

### 🏆 Usage

```shell
hostctl CLI

Usage:
hostctl [OPTIONS]
hostctl <COMMAND> [ARGS]

Commands:
  list              List host entries
  add               Add a new host entry
  edit              Edit an existing host entry
  remove            Remove a host entry
  tags              List all tags currently in use
  backup            Create a timestamped backup of the hosts file
  help              Print this message or the help of the given subcommand(s)

Options:
  -h, --help
          Print help (see a summary with '-h')

  -V, --version
          Print version

Notes:
  add, edit, remove, and backup require Administrator privileges (they write to the hosts file).
  Every write creates a timestamped backup and prints its path: <hosts>.bak_yyyyMMdd_HHmmss
  Exit codes: 0 = success, 1 = error, 2 = invalid usage/arguments.
  Tags metadata: C:\ProgramData\hostctl\hosts.tags.json
  Hosts file    : C:\WINDOWS\system32\drivers\etc\hosts
```

Run `hostctl <command> --help` (e.g. `hostctl remove --help`) for a command's full flag list — every command supports `-h`/`--help` and `-V`/`--version`.

#### Examples

```shell
hostctl list --tag web --sort ip
hostctl list --all --json
hostctl add --ip 10.0.0.5 --host api.local --tag web,dev
hostctl edit --host api.local --ip 10.0.0.6 --dry-run
hostctl remove --host api.local --yes
```

### ⚡ Changelog

**2.0.0** — breaking changes:

- `-v` (lowercase) is no longer an alias for `--version` — use `-V` (uppercase) or `--version`. Lowercase `-v` is reserved for a possible future `--verbose` flag.
- Exit codes: `2` now means invalid usage/arguments (e.g. a missing required flag or an unrecognized `--sort` value), `1` is a runtime error, `0` is success — previously every failure exited `1`.
- `list --all` previously did nothing (a parsing bug always hid commented-out entries); it now genuinely shows them, prefixed with `#`.

### 📜 License

This project is licensed under the [MIT License](LICENSE).

### ✉️ Contact

**Natthasath Saksupanara** — Computer Technical Officer, NIDA  
natthasath.sak@gmail.com
