# SshHelper

Windows SSH 服务管理工具 — WPF 图形界面，用于管理 OpenSSH 服务、生成和部署 SSH 密钥。

## 功能

- 一键启动/停止/重启 Windows OpenSSH Server (`sshd`)
- SSH 密钥对生成（`ssh-keygen`）
- 公钥自动部署到 authorized_keys 和 administrators_authorized_keys
- 密钥备份与轮换
- 私钥复制（文本/文件拖放）
- SSH 连接测试
- 开机自启服务配置

## 使用要求

- **管理员权限**：本程序需要以管理员身份运行（见 `app.manifest` 中的 `requireAdministrator`），因为它需要管理 Windows 服务及写入 `C:\ProgramData\ssh\` 目录。
- **Windows OpenSSH Server**：需预先安装 OpenSSH 可选功能（`Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0`）。
- **.NET 8.0** 运行时。

## 免责声明

> 本软件按「原样」提供，不提供任何明示或暗示的保证，包括但不限于适销性、特定用途适用性和非侵权性的保证。在任何情况下，作者均不对因使用本软件而产生的任何索赔、损害或其他责任负责。
>
> 本软件涉及对 Windows SSH 服务的底层操作（启动/停止服务、修改系统 SSH 配置、操作管理员授权密钥文件），使用者应了解相关操作的含义并自行承担风险。

## 构建

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

## License

MIT
