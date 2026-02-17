# QcnTool.Cli

## 中文说明

### 项目简介
`QcnTool.Cli` 是一个用于 Qualcomm 设备 QCN 读写的命令行工具，支持：

- `--info`：读取并打印设备基础信息
- `--read`：从设备备份 QCN 到本地文件
- `--write`：将本地 QCN 写回设备

工具默认支持中英文输出，并可自动识别 Qualcomm DIAG 端口。

### 运行环境

- Windows（项目为 `x86` 目标）
- 设备已启用 DIAG，并安装可用的 Qualcomm 驱动
- 可识别到 Qualcomm 端口（VID `05C6`）
- 构建源码需要 .NET SDK 10（`net10.0`）

> 警告：QCN 写入具有风险，仅应在你有权限且可恢复的设备上操作。

### 构建与发布

```powershell
dotnet build -c Release -p:Platform=x86
dotnet publish -c Release -p:PublishProfile=FolderProfile
```

发布输出目录（按当前配置）：

- `bin\QcnTool\QcnTool.Cli.exe`

### 命令用法

```text
--write --file <input.qcn> [--port COM4] [--lang zh|en|auto]
--read  [--file output.qcn] [--port COM4] [--lang zh|en|auto]
--info  [--port COM4] [--lang zh|en|auto]
--help
--version
```

### 参数说明

- `-w`, `--write`：写入模式（必须配合 `--file`）
- `-r`, `--read`：读取模式（`--file` 可省略）
- `-i`, `--info`：信息模式（不使用 `--file`）
- `-f`, `--file`：QCN 输入或输出路径
- `-p`, `--port`：指定端口，支持 `COM4` 或 `4`
- `-l`, `--lang`：语言 `zh` / `en` / `auto`
- `-h`, `--help`：显示帮助
- `-v`, `--version`：显示版本

### 使用示例

```powershell
# 自动识别端口，输出设备信息
QcnTool.Cli.exe --info

# 指定端口读取 QCN
QcnTool.Cli.exe --read --port COM4 --file backup.qcn

# 自动识别端口并按默认命名输出（QCN_yyyyMMdd_HHmmss.qcn）
QcnTool.Cli.exe --read

# 指定端口写入 QCN
QcnTool.Cli.exe --write --port COM4 --file restore.qcn

# 强制英文输出
QcnTool.Cli.exe --info --lang en
```

### 行为说明

- 未指定 `--port` 时，工具会按 Qualcomm VID/MI 规则自动识别端口。
- `--write` 会校验输入文件是否存在。
- `--read` 未指定 `--file` 时，输出到当前目录并自动生成文件名。
- `--info` 模式不会执行写入相关操作。
- 当前实现中，SPC 固定使用 `000000`。

### 退出码

- `0`：成功
- `1`：失败

---

## English

### Overview
`QcnTool.Cli` is a command-line utility for Qualcomm QCN operations on Windows:

- `--info`: read and print device information
- `--read`: backup QCN from device to local file
- `--write`: restore/write QCN from local file to device

It supports bilingual output (Chinese/English) and automatic DIAG port detection.

### Requirements

- Windows (`x86` target)
- Device with DIAG enabled and working Qualcomm driver
- Detectable Qualcomm port with VID `05C6`
- .NET SDK 10 (`net10.0`) to build from source

> Warning: Writing QCN is risky. Use only on devices you are authorized to service.

### Build and Publish

```powershell
dotnet build -c Release -p:Platform=x86
dotnet publish -c Release -p:PublishProfile=FolderProfile
```

Publish output (current profile):

- `bin\QcnTool\QcnTool.Cli.exe`

### Usage

```text
--write --file <input.qcn> [--port COM4] [--lang zh|en|auto]
--read  [--file output.qcn] [--port COM4] [--lang zh|en|auto]
--info  [--port COM4] [--lang zh|en|auto]
--help
--version
```

### Arguments

- `-w`, `--write`: write mode (requires `--file`)
- `-r`, `--read`: read mode (`--file` optional)
- `-i`, `--info`: info mode (`--file` not used)
- `-f`, `--file`: input/output QCN path
- `-p`, `--port`: port value, accepts `COM4` or `4`
- `-l`, `--lang`: `zh` / `en` / `auto`
- `-h`, `--help`: show help
- `-v`, `--version`: show version

### Examples

```powershell
QcnTool.Cli.exe --info
QcnTool.Cli.exe --read --port COM4 --file backup.qcn
QcnTool.Cli.exe --read
QcnTool.Cli.exe --write --port COM4 --file restore.qcn
QcnTool.Cli.exe --info --lang en
```

### Notes

- If `--port` is omitted, the tool auto-selects a Qualcomm DIAG port by VID/MI rules.
- `--write` validates the input file path.
- `--read` generates `QCN_yyyyMMdd_HHmmss.qcn` in current directory when `--file` is omitted.
- `--info` does not perform write operations.
- Current implementation uses fixed SPC `000000`.

### Exit Codes

- `0`: success
- `1`: failure
