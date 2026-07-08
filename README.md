# SshHelper

Windows SSH 服务管理工具 — WPF 图形界面，用于管理 OpenSSH 服务、生成和部署 SSH 密钥。

## 功能

### 一键配置 (v1.01+)
- 自动检查并安装 OpenSSH Client/Server（通过 Windows Capability）
- 自动检测 sshd 服务状态，安装后自动启动并设为开机自启
- 缺失密钥时自动生成 ED25519 密钥对
- 自动部署公钥到 `authorized_keys` 和 `administrators_authorized_keys`
- 自动配置 ACL 权限（禁用继承、仅 SYSTEM + Administrators 可读）
- 自动测试本机密钥登录

### 密钥管理
- SSH 密钥对生成（`ssh-keygen -t ed25519`）
- 密钥备份与轮换（自动打包到 `bak/` 目录）
- 公钥自动部署（兼容 Windows OpenSSH 管理员组重定向）
- 私钥复制：文本复制 + 文件拖放复制（Win32 API，绕过管理员权限限制）

### SSH 连接测试
- 测试本机密钥登录
- 显示详细错误信息

### 智能路径查找 (v1.01+)
- 自动搜索 `ssh-keygen`/`ssh` 位置：System32 → ProgramFiles → Git for Windows → PATH
- 不再依赖系统 PATH 环境变量

### 优雅降级 (v1.01+)
- 所有 ACL 操作、文件写入、服务控制均有 try/catch 保护
- 单步失败不阻塞整体流程
- 详细的彩色日志输出

## 使用要求

- **管理员权限**：本程序需要以管理员身份运行（见 `app.manifest` 中的 `requireAdministrator`），因为它需要管理 Windows 服务及写入 `C:\ProgramData\ssh\` 目录。
- **.NET 8.0** 运行时（self-contained 发布版本无需安装）。
- OpenSSH Client/Server 可由程序自动安装，无需提前准备。

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
