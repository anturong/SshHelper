using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;

namespace SshHelper;

public partial class MainWindow : Window
{
    private string PrivateKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");
    private string PubKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519.pub");
    private string AuthKeysPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "authorized_keys");
    private string AuthKeysAdminPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ssh", "administrators_authorized_keys");
    private string BakDir = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "bak");

    public MainWindow()
    {
        InitializeComponent();
        TxtUser.Text = Environment.UserName;
        TxtIp.Text = GetLocalIP();
    }

    // ==================== UI 交互逻辑 ====================
    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        BtnStart.IsEnabled = false;
        LogConsole.Document.Blocks.Clear();
        await Task.Run(() => RunFullConfiguration());
        BtnStart.IsEnabled = true;
    }

    private async void BtnTest_Click(object sender, RoutedEventArgs e)
    {
        BtnTest.IsEnabled = false;
        LogConsole.Document.Blocks.Clear();
        await Task.Run(() => TestSshConnection());
        BtnTest.IsEnabled = true;
    }

    private void ToggleKey_Click(object sender, RoutedEventArgs e)
    {
        if (TxtPrivateKey.Visibility == Visibility.Collapsed)
        {
            if (File.Exists(PrivateKeyPath))
            {
                TxtPrivateKey.Text = File.ReadAllText(PrivateKeyPath);
                TxtPrivateKey.Visibility = Visibility.Visible;
            }
            else
            {
                AppendLog("私钥文件不存在，无法显示。\n", "Red");
            }
        }
        else
        {
            TxtPrivateKey.Visibility = Visibility.Collapsed;
        }
    }

    private void CopyKeyText_Click(object sender, RoutedEventArgs e)
    {
        if (File.Exists(PrivateKeyPath))
        {
            if (Win32ClipboardSetText(File.ReadAllText(PrivateKeyPath)))
                MessageBox.Show("私钥文本已复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            else
                MessageBox.Show("复制失败：剪贴板被其他进程占用，请重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show("私钥文件不存在！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CopyKeyFile_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(PrivateKeyPath))
        {
            MessageBox.Show("私钥文件不存在！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (Win32ClipboardSetFileDropList(PrivateKeyPath))
            MessageBox.Show("私钥文件已复制到剪贴板，可在资源管理器中粘贴！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show("复制失败：剪贴板被其他进程占用，请重试。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void ReplaceKey_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "即将执行以下操作：\n\n" +
            "1. 备份现有密钥到 bak 文件夹\n" +
            "2. 生成新的 ED25519 密钥对\n" +
            "3. 部署新公钥（authorized_keys + administrators_authorized_keys）\n" +
            "4. 测试新密钥登录\n" +
            "5. 复制新私钥到剪贴板\n\n" +
            "⚠ 确认继续？",
            "更换密钥", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        BtnStart.IsEnabled = false;
        LogConsole.Document.Blocks.Clear();
        await Task.Run(() => RunReplaceKey());
        BtnStart.IsEnabled = true;
    }

    private void CopyText(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            Clipboard.SetText(tb.Text);
            AppendLog($"已复制: {tb.Text}\n", "Cyan");
        }
    }

    // ==================== 更换密钥逻辑 ====================
    private void RunReplaceKey()
    {
        try
        {
            AppendLog("=== 1/5 备份现有密钥 ===\n", "Cyan");
            BackupKeys();

            AppendLog("\n=== 2/5 生成新密钥 ===\n", "Cyan");
            GenerateNewKeys();

            AppendLog("\n=== 3/5 部署新公钥 ===\n", "Cyan");
            DeployPublicKey();

            AppendLog("\n=== 4/5 验证新密钥 ===\n", "Cyan");
            TestSshConnection();

            AppendLog("\n=== 5/5 复制新私钥到剪贴板 ===\n", "Cyan");
            CopyNewPrivateKey();
        }
        catch (Exception ex)
        {
            AppendLog($"\n更换密钥失败：{ex.Message}\n", "Red");
        }
    }

    private void BackupKeys()
    {
        var sshDir = Path.GetDirectoryName(PrivateKeyPath);
        if (sshDir is null || !Directory.Exists(sshDir))
        {
            AppendLog("~/.ssh 目录不存在，跳过备份\n", "Yellow");
            return;
        }

        if (!Directory.Exists(BakDir))
            Directory.CreateDirectory(BakDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(BakDir, $"{timestamp}.zip");

        ZipFile.CreateFromDirectory(sshDir, zipPath);
        AppendLog($"已备份到: {zipPath}\n", "Green");
    }

    private void GenerateNewKeys()
    {
        // 清理旧密钥
        if (File.Exists(PrivateKeyPath)) File.Delete(PrivateKeyPath);
        if (File.Exists(PubKeyPath)) File.Delete(PubKeyPath);

        var psi = new ProcessStartInfo
        {
            FileName = "ssh-keygen",
            Arguments = $"-t ed25519 -f \"{PrivateKeyPath}\" -N \"\" -q",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var process = Process.Start(psi);
        if (process is null) { AppendLog("无法启动 ssh-keygen\n", "Red"); return; }
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            AppendLog("密钥生成失败: " + process.StandardError.ReadToEnd() + "\n", "Red");
            return;
        }
        AppendLog("新 ED25519 密钥对已生成\n", "Green");
    }

    private void DeployPublicKey()
    {
        if (!File.Exists(PubKeyPath))
        {
            AppendLog("公钥文件不存在，无法部署\n", "Red");
            return;
        }

        string pubKey = File.ReadAllText(PubKeyPath).Trim();

        // 写入 ~/.ssh/authorized_keys
        var sshDir = Path.GetDirectoryName(AuthKeysPath);
        if (sshDir is not null && !Directory.Exists(sshDir)) Directory.CreateDirectory(sshDir);
        GrantWriteAccess(AuthKeysPath);
        File.WriteAllText(AuthKeysPath, pubKey + Environment.NewLine);
        AppendLog("公钥已写入 authorized_keys\n", "Green");

        // 写入 C:\ProgramData\ssh\administrators_authorized_keys
        var adminSshDir = Path.GetDirectoryName(AuthKeysAdminPath);
        if (adminSshDir is not null && !Directory.Exists(adminSshDir)) Directory.CreateDirectory(adminSshDir);
        GrantWriteAccess(AuthKeysAdminPath);
        File.WriteAllText(AuthKeysAdminPath, pubKey + Environment.NewLine);
        AppendLog("公钥已写入 administrators_authorized_keys\n", "Green");

        // 设置权限
        SetAuthorizedKeysPermission();
        SetAdminAuthorizedKeysPermission();
    }

    // 为当前用户添加写权限，用于覆盖之前设的只读 ACL
    private void GrantWriteAccess(string path)
    {
        if (!File.Exists(path)) return;
        var fileInfo = new FileInfo(path);
        var security = fileInfo.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().Name,
            FileSystemRights.Write,
            AccessControlType.Allow));
        fileInfo.SetAccessControl(security);
    }

    // 复制新私钥到剪贴板（仅在更换密钥流程中调用）
    private void CopyNewPrivateKey()
    {
        if (!File.Exists(PrivateKeyPath))
        {
            AppendLog("私钥文件不存在，无法复制\n", "Red");
            return;
        }

        Dispatcher.Invoke(() =>
        {
            var text = File.ReadAllText(PrivateKeyPath);
            if (Win32ClipboardSetText(text))
            {
                AppendLog("新私钥已复制到剪贴板，请妥善保管！\n", "Green");
            }
            else
            {
                AppendLog("复制到剪贴板失败：剪贴板被其他进程占用\n", "Red");
                AppendLog("新私钥保存在: " + PrivateKeyPath + "\n", "Yellow");
            }
        });
    }

    // 用 Win32 API 直接设置剪贴板文本，比 WPF Clipboard.SetText 更可靠
    private static bool Win32ClipboardSetText(string text)
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                    IntPtr ptr = Marshal.StringToHGlobalUni(text);
                    if (SetClipboardData(13, ptr) == IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(ptr);
                        return false;
                    }
                    return true; // 剪贴板接管了内存，不要 Free
                }
                finally
                {
                    CloseClipboard();
                }
            }
            System.Threading.Thread.Sleep(200);
        }
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DROPFILES
    {
        public uint pFiles;
        public int pt_x;
        public int pt_y;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fNC;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fWide;
    }

    private static bool Win32ClipboardSetFileDropList(string filePath)
    {
        for (int i = 0; i < 10; i++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();

                    byte[] files = Encoding.Unicode.GetBytes(filePath + "\0\0");
                    var dropfiles = new DROPFILES
                    {
                        pFiles = (uint)Marshal.SizeOf<DROPFILES>(),
                        fWide = true
                    };
                    int structSize = Marshal.SizeOf<DROPFILES>();
                    byte[] structBytes = new byte[structSize];
                    IntPtr p = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        Marshal.StructureToPtr(dropfiles, p, false);
                        Marshal.Copy(p, structBytes, 0, structSize);

                        byte[] data = new byte[structSize + files.Length];
                        Buffer.BlockCopy(structBytes, 0, data, 0, structSize);
                        Buffer.BlockCopy(files, 0, data, structSize, files.Length);

                        IntPtr hGlobal = Marshal.AllocHGlobal(data.Length);
                        Marshal.Copy(data, 0, hGlobal, data.Length);

                        if (SetClipboardData(0xF, hGlobal) == IntPtr.Zero) // 0xF = CF_HDROP
                        {
                            Marshal.FreeHGlobal(hGlobal);
                            return false;
                        }
                        return true;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(p);
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }
            System.Threading.Thread.Sleep(200);
        }
        return false;
    }

    private void SetAdminAuthorizedKeysPermission()
    {
        if (!File.Exists(AuthKeysAdminPath)) return;
        var fileInfo = new FileInfo(AuthKeysAdminPath);
        var security = fileInfo.GetAccessControl();

        // 禁用继承，清除所有继承权限
        security.SetAccessRuleProtection(true, false);

        // SYSTEM 读取权限
        security.SetAccessRule(new FileSystemAccessRule("SYSTEM",
            FileSystemRights.Read, AccessControlType.Allow));

        // BUILTIN\Administrators 读取权限
        security.SetAccessRule(new FileSystemAccessRule("BUILTIN\\Administrators",
            FileSystemRights.Read, AccessControlType.Allow));

        fileInfo.SetAccessControl(security);
        AppendLog("administrators_authorized_keys ACL 权限设置完成\n", "Green");
    }

    // ==================== 核心配置逻辑 ====================
    private void RunFullConfiguration()
    {
        try
        {
            // 1. 检查并安装 OpenSSH
            AppendLog("=== 检查 OpenSSH 组件 ===\n", "Cyan");
            RunPowershellCommand("Get-WindowsCapability -Online | Where-Object { $_.Name -like 'OpenSSH.Client*' -and $_.State -ne 'Installed' } | Add-WindowsCapability -Online", "安装 OpenSSH Client...");
            RunPowershellCommand("Get-WindowsCapability -Online | Where-Object { $_.Name -like 'OpenSSH.Server*' -and $_.State -ne 'Installed' } | Add-WindowsCapability -Online", "安装 OpenSSH Server...");

            // 2. 检查 sshd 服务
            AppendLog("\n=== 检查 sshd 服务状态 ===\n", "Cyan");
            var sshd = new ServiceController("sshd");
            if (sshd.Status != ServiceControllerStatus.Running)
            {
                AppendLog("设置 sshd 为自动启动...\n", "Yellow");
                RunPowershellCommand("sc config sshd start=auto", "sshd 已设为自动启动");
                AppendLog("启动 sshd 服务...\n", "Yellow");
                sshd.Start();
                sshd.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
            AppendLog("sshd 服务运行正常\n", "Green");
            AppendLog("确保 sshd 启动类型为自动...\n", "Yellow");
            RunPowershellCommand("sc config sshd start=auto", "sshd 启动类型已确认");

            // 2b. 检查 sshd_config 中 Administrators 组的重定向设置
            AppendLog("\n=== 检查 sshd_config 配置 ===\n", "Cyan");
            var sshdConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "OpenSSH", "sshd_config");
            if (!File.Exists(sshdConfigPath))
                sshdConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ssh", "sshd_config");
            if (File.Exists(sshdConfigPath))
            {
                var config = File.ReadAllText(sshdConfigPath);
                if (config.Contains("Match Group Administrators") || config.Contains("administrators_authorized_keys"))
                {
                    AppendLog("检测到 sshd_config 为 Administrators 组配置了专用授权文件\n", "Yellow");
                    AppendLog("→ 程序将同步写公钥到 administrators_authorized_keys\n", "Cyan");
                }
            }

            // 3. 检查私钥
            AppendLog("\n=== 检查密钥文件 ===\n", "Cyan");
            if (!File.Exists(PrivateKeyPath))
            {
                AppendLog($"错误：私钥文件不存在：{PrivateKeyPath}\n", "Red");
                AppendLog("请在终端运行生成密钥：ssh-keygen -t ed25519\n", "Yellow");
                return;
            }

            // 4. 配置 authorized_keys + administrators_authorized_keys
            AppendLog("\n=== 配置公钥授权 ===\n", "Cyan");
            if (File.Exists(PubKeyPath))
            {
                string pubKey = File.ReadAllText(PubKeyPath).Trim();

                // 4a. ~/.ssh/authorized_keys
                string? sshDir = Path.GetDirectoryName(AuthKeysPath);
                if (sshDir is not null && !Directory.Exists(sshDir)) Directory.CreateDirectory(sshDir);

                bool keyExists = false;
                if (File.Exists(AuthKeysPath))
                {
                    keyExists = File.ReadAllLines(AuthKeysPath).Any(line => line.Trim() == pubKey);
                }

                if (!keyExists)
                {
                    File.AppendAllText(AuthKeysPath, pubKey + Environment.NewLine);
                    AppendLog("公钥已添加到 authorized_keys\n", "Green");
                }
                else
                {
                    AppendLog("公钥已存在 authorized_keys，跳过\n", "Yellow");
                }

                // 4b. C:\ProgramData\ssh\administrators_authorized_keys
                string? adminSshDir = Path.GetDirectoryName(AuthKeysAdminPath);
                if (adminSshDir is not null && !Directory.Exists(adminSshDir)) Directory.CreateDirectory(adminSshDir);

                bool adminKeyExists = false;
                if (File.Exists(AuthKeysAdminPath))
                {
                    adminKeyExists = File.ReadAllLines(AuthKeysAdminPath).Any(line => line.Trim() == pubKey);
                }

                if (!adminKeyExists)
                {
                    File.WriteAllText(AuthKeysAdminPath, pubKey + Environment.NewLine);
                    AppendLog("公钥已写入 administrators_authorized_keys\n", "Green");
                }
                else
                {
                    AppendLog("公钥已存在 administrators_authorized_keys，跳过\n", "Yellow");
                }
            }
            else
            {
                AppendLog("警告：找不到公钥文件，跳过授权配置\n", "Yellow");
            }

            // 5. 设置权限
            AppendLog("\n=== 设置文件权限 ===\n", "Cyan");
            SetAuthorizedKeysPermission();
            SetAdminAuthorizedKeysPermission();

            // 6. 测试连接
            AppendLog("\n=== 测试本机密钥登录 ===\n", "Cyan");
            TestSshConnection();
        }
        catch (Exception ex)
        {
            AppendLog($"\n执行出错：{ex.Message}\n", "Red");
        }
    }

    private void TestSshConnection()
    {
        AppendLog($"正在测试 {Environment.UserName}@127.0.0.1 ...\n", "Yellow");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh",
                Arguments = $"-o StrictHostKeyChecking=accept-new -o ConnectTimeout=5 -o BatchMode=yes -i \"{PrivateKeyPath}\" {Environment.UserName}@127.0.0.1 exit",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(psi);
            if (process is null) { AppendLog("无法启动 ssh 进程\n", "Red"); return; }
            process.WaitForExit(10000);
            if (process.ExitCode == 0)
            {
                AppendLog("\n本机密钥登录成功！配置正确！\n", "Green");
            }
            else
            {
                AppendLog("\n本机密钥登录失败，请检查 sshd_config 设置\n", "Red");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"\n测试失败：{ex.Message}\n", "Red");
        }
    }

    // ==================== 辅助方法 ====================
    private void SetAuthorizedKeysPermission()
    {
        if (!File.Exists(AuthKeysPath)) return;
        var fileInfo = new FileInfo(AuthKeysPath);
        var security = fileInfo.GetAccessControl();

        // 禁用继承并移除现有规则
        security.SetAccessRuleProtection(true, false);

        // 仅添加当前用户的读取权限
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().Name,
                                            FileSystemRights.Read,
                                            AccessControlType.Allow);
        security.SetAccessRule(rule);

        fileInfo.SetAccessControl(security);
        AppendLog("ACL 权限设置完成\n", "Green");
    }

    private void RunPowershellCommand(string script, string successMsg)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"{script}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var process = Process.Start(psi);
        if (process is null) { AppendLog("无法启动 PowerShell 进程\n", "Red"); return; }
        process.WaitForExit();
        if (process.ExitCode == 0)
        {
            if (!string.IsNullOrEmpty(successMsg)) AppendLog(successMsg + " [完成]\n", "Green");
        }
        else
        {
            AppendLog("执行失败: " + process.StandardError.ReadToEnd() + "\n", "Red");
        }
    }

    private string GetLocalIP()
    {
        try
        {
            var entry = Dns.GetHostEntry(Dns.GetHostName());
            var ip = entry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
            return ip?.ToString() ?? "未获取到IP";
        }
        catch
        {
            return "未获取到IP (请ipconfig查看)";
        }
    }

    // 线程安全的彩色日志追加
    private void AppendLog(string text, string color)
    {
        Dispatcher.Invoke(() =>
        {
            var run = new Run(text);
            switch (color)
            {
                case "Green": run.Foreground = System.Windows.Media.Brushes.LightGreen; break;
                case "Yellow": run.Foreground = System.Windows.Media.Brushes.Yellow; break;
                case "Red": run.Foreground = System.Windows.Media.Brushes.LightPink; break;
                case "Cyan": run.Foreground = System.Windows.Media.Brushes.Cyan; break;
                default: run.Foreground = System.Windows.Media.Brushes.White; break;
            }
            var paragraph = new Paragraph(run) { Margin = new Thickness(0) };
            LogConsole.Document.Blocks.Add(paragraph);
            LogConsole.ScrollToEnd();
        });
    }
}
