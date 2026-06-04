using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Runtime.CompilerServices; // 用于隔离 JIT 编译，防止缺 DLL 时闪退
using DokanNet;
using DokanNet.Logging;

namespace AndroidVirtualDrive
{
    class Program
    {
        static void Main(string[] args)
        {
            // 启动时选择语言
            SelectLanguage();

            int port = 8080;
            char driveLetter = 'Z';
            string mountPoint = $"{driveLetter}:\\";
            string targetIp = "127.0.0.1";

            while (true)
            {
                Console.Clear();
                Console.WriteLine("==================================================");
                Console.WriteLine("                FuckAndroidMTP");
                Console.WriteLine(Language.L("      MTP是垃圾，这是真正的物理级映射磁盘！", "      MTP is trash, this is a real physical-level mapped drive!"));
                Console.WriteLine("==================================================");

                // --- 1. 环境预检 ---
                bool hasAdb = IsAdbInstalled();
                bool hasDokan = IsDokanInstalled();

                Console.WriteLine(Language.L("【运行环境状态】:", "【Environment Status】:"));
                Console.WriteLine(hasAdb ? Language.L("  [√] ADB 调试工具 已就绪", "  [√] ADB Toolkit Ready") : Language.L("  [X] ADB 调试工具 未安装", "  [X] ADB Toolkit Not Installed"));
                Console.WriteLine(hasDokan ? Language.L("  [√] Dokan 虚拟磁盘驱动 已就绪", "  [√] Dokan Virtual Disk Driver Ready") : Language.L("  [X] Dokan 虚拟磁盘驱动 未安装", "  [X] Dokan Virtual Disk Driver Not Installed"));
                Console.WriteLine("--------------------------------------------------");

                Console.WriteLine(Language.L("请选择操作:", "Please select an operation:"));
                Console.WriteLine(Language.L("[1] USB 极速模式 (需要插线，自动配置 ADB 隧道)", "[1] USB Speed Mode (Cable required, auto-configures ADB tunnel)"));
                Console.WriteLine(Language.L("[2] Wi-Fi 无线模式 (需要手机和电脑在同一局域网)", "[2] Wi-Fi Wireless Mode (Phone and PC must be on the same LAN)"));
                Console.WriteLine(Language.L("[3] 一键安装运行环境 (自动下载配置 ADB 与 Dokan)", "[3] 1-Click Install Environment (Auto downloads & configs ADB and Dokan)"));
                Console.WriteLine(Language.L("[0] 退出", "[0] Exit"));

                Console.Write(Language.L("\n请输入数字 (0-3): ", "\nPlease enter a number (0-3): "));

                string choice = Console.ReadLine();

                if (choice == "0") return;

                if (choice == "3")
                {
                    InstallEnvironment(hasAdb, hasDokan);
                    Console.WriteLine(Language.L("\n按任意键返回主菜单...", "\nPress any key to return to the main menu..."));
                    Console.ReadKey();
                    continue; // 返回主菜单重新选择
                }

                // --- 2. 拦截未安装环境的用户 ---
                if (choice == "1" || choice == "2")
                {
                    if (!hasAdb || !hasDokan)
                    {
                        Console.WriteLine(Language.L("\n[-] 运行环境缺失！请先按 [3] 一键安装运行环境。", "\n[-] Missing environment! Please press [3] to install first."));
                        Console.WriteLine(Language.L("按任意键返回...", "Press any key to return..."));
                        Console.ReadKey();
                        continue;
                    }

                    if (choice == "2")
                    {
                        Console.Write(Language.L("请输入安卓手机的局域网 IP 地址 (例如 192.168.1.100): ", "Please enter the Android phone's LAN IP address (e.g., 192.168.1.100): "));
                        targetIp = Console.ReadLine()?.Trim();
                        if (string.IsNullOrEmpty(targetIp))
                        {
                            Console.WriteLine(Language.L("[-] IP地址不可为空。", "[-] IP address cannot be empty."));
                            Thread.Sleep(1500);
                            continue;
                        }
                        Console.WriteLine(Language.L($"[*] 将通过 Wi-Fi 直连手机 -> {targetIp}:{port}", $"[*] Will connect directly to phone via Wi-Fi -> {targetIp}:{port}"));
                    }
                    else if (choice == "1")
                    {
                        Console.WriteLine(Language.L("[*] 正在设置 ADB 端口转发 (USB -> 局域网 TCP)...", "[*] Setting up ADB port forwarding (USB -> LAN TCP)..."));
                        if (!SetupAdbForward(port))
                        {
                            Console.WriteLine(Language.L("\n请按任意键返回...", "\nPress any key to return..."));
                            Console.ReadKey();
                            continue;
                        }
                        targetIp = "127.0.0.1";
                    }

                    // --- 3. 安全调用隔离出去的挂载逻辑 ---
                    Console.WriteLine(Language.L($"[*] 正在启动挂载引擎...", $"[*] Starting mount engine..."));
                    RunMountEngine(targetIp, port, driveLetter, mountPoint);
                    break; // 挂载结束或手动卸载后，安全退出程序
                }
            }
        }

        // ==========================================
        // 核心隔离区：禁止编译器把这部分代码内联到 Main 中！
        // ==========================================
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RunMountEngine(string targetIp, int port, char driveLetter, string mountPoint)
        {
            try
            {
                using (var drive = new DokanAndroidDrive())
                {
                    Console.WriteLine(Language.L($"[*] 正在连接到 {targetIp}:{port} ...", $"[*] Connecting to {targetIp}:{port} ..."));
                    drive.Connect(targetIp, port);

                    using (var dokan = new Dokan(new NullLogger()))
                    {
                        var builder = new DokanInstanceBuilder(dokan)
                            .ConfigureOptions(options =>
                            {
                                options.Options = DokanOptions.RemovableDrive;
                                options.MountPoint = mountPoint;
                            });

                        Console.WriteLine(Language.L($"[*] 准备将安卓目录挂载为 {driveLetter}: 盘...", $"[*] Preparing to mount Android directory as drive {driveLetter}: ..."));

                        var waitHandle = new ManualResetEvent(false);

                        // 监听断开事件
                        drive.OnDisconnected += () =>
                        {
                            Console.WriteLine(Language.L("\n[!] 检测到安卓端服务已停止或切换，正在自动断开并清理...", "\n[!] Android service stopped or switched. Automatically disconnecting and cleaning up..."));
                            waitHandle.Set();
                        };

                        // 监听 Ctrl+C 手动退出
                        Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) =>
                        {
                            Console.WriteLine(Language.L("\n[*] 接收到手动退出信号，正在安全卸载虚拟磁盘...", "\n[*] Manual exit signal received, safely unmounting virtual drive..."));
                            e.Cancel = true;
                            waitHandle.Set();
                        };

                        using (var dokanInstance = builder.Build(drive))
                        {
                            Console.WriteLine(Language.L($"\n[+] 挂载成功！你现在可以打开我的电脑，干碎 MTP 了！", $"\n[+] Mount successful! You can now open 'My Computer' and smash MTP!"));
                            Console.WriteLine(Language.L($"[!] 若要手动安全卸载磁盘，请在此窗口按 [Ctrl + C]", $"[!] To manually and safely unmount the drive, press [Ctrl + C] in this window."));
                            Console.WriteLine("--------------------------------------------------");

                            // 阻塞在这里，直到 Ctrl+C 或者 手机端断开连接
                            waitHandle.WaitOne();
                        }
                    }
                    Console.WriteLine(Language.L("[+] FuckAndroidMTP 磁盘已安全卸载。", "[+] FuckAndroidMTP drive safely unmounted."));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(Language.L($"\n[-] 挂载引擎发生异常: {ex.Message}", $"\n[-] Mount engine exception: {ex.Message}"));
                if (ex is DokanException || ex is DllNotFoundException)
                {
                    Console.WriteLine(Language.L("[-] 缺少底层驱动！请重启软件并选择 [3] 一键安装运行环境。", "[-] Missing underlying driver! Please restart and select [3] 1-Click Install Environment."));
                }
                Console.WriteLine(Language.L("\n按任意键返回...", "\nPress any key to return..."));
                Console.ReadKey();
            }
        }

        static void SelectLanguage()
        {
            Console.Clear();
            Console.WriteLine("Please select language / 请选择语言:");
            Console.WriteLine("[1] 中文 (Chinese)");
            Console.WriteLine("[2] English");
            Console.WriteLine("[3] 中英双语 (Bilingual)");
            Console.Write("\nChoice / 选择 (1-3): ");

            string choice = Console.ReadLine();
            if (choice == "2") Language.currentLang = Lang.EN;
            else if (choice == "3") Language.currentLang = Lang.Bilingual;
            else Language.currentLang = Lang.ZH; // 默认使用中文
        }
        static void RefreshEnvironmentPath()
        {
            try
            {
                // 从注册表中强行拉取最新的系统和用户 PATH 变量，合并后覆盖当前 C# 进程的老快照
                // 这样即使刚用 winget 装完，程序也能立刻识别到，无需重启软件！
                string machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
                string userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
                Environment.SetEnvironmentVariable("PATH", $"{machinePath};{userPath}");
            }
            catch { }
        }
        // ==========================================
        // 环境检测与自动化部署模块
        // ==========================================
        static bool IsAdbInstalled()
        {
            RefreshEnvironmentPath(); // 检查前，先深呼吸刷新一下环境变量
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = "adb.exe", // 【修复】：严格加上 .exe 后缀
                    Arguments = "version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        static bool IsDokanInstalled()
        {
            string system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            return File.Exists(Path.Combine(system32, "dokan2.dll"));
        }

        static void InstallEnvironment(bool hasAdb, bool hasDokan)
        {
            Console.WriteLine(Language.L("\n[*] 准备通过 winget 一键安装依赖环境...", "\n[*] Preparing to install dependencies via winget..."));
            Console.WriteLine(Language.L("[*] 注意：期间可能会弹出 UAC 管理员权限提示，请点击允许。\n", "[*] Note: UAC admin prompts may appear, please click Allow.\n"));

            if (!hasAdb)
                RunWinget("Google.PlatformTools", Language.L("ADB 工具箱 (Google Platform Tools)", "ADB Toolkit (Google Platform Tools)"));
            else
                Console.WriteLine(Language.L("[+] ADB 工具箱已安装，跳过。", "[+] ADB Toolkit already installed, skipping."));

            if (!hasDokan)
                RunWinget("dokan-dev.Dokany", Language.L("Dokan 虚拟磁盘驱动", "Dokan Virtual Disk Driver"));
            else
                Console.WriteLine(Language.L("[+] Dokan 驱动已安装，跳过。", "[+] Dokan Virtual Disk Driver already installed, skipping."));

            Console.WriteLine(Language.L("\n[+] 环境部署流程结束！", "\n[+] Environment deployment finished!"));
            Console.WriteLine(Language.L("[!] 强烈建议：Dokan 驱动安装完毕后，请【重启电脑】以使内核驱动生效，然后再运行本程序！", "[!] HIGHLY RECOMMENDED: After installing Dokan driver, please [RESTART YOUR PC] to apply the kernel driver, then run this program again!"));
        }

        static void RunWinget(string packageId, string name)
        {
            Console.WriteLine(Language.L($"\n>>> 正在获取 {name} ...", $"\n>>> Fetching {name} ..."));
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"install --id \"{packageId}\" --exact --source winget --accept-package-agreements --accept-source-agreements",
                    UseShellExecute = false,
                };

                var process = Process.Start(psi);
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Console.WriteLine(Language.L($"[+] {name} 安装成功！", $"[+] {name} installed successfully!"));
                }
                else
                {
                    Console.WriteLine(Language.L($"[-] {name} 安装中止或已存在，退出码: {process.ExitCode}", $"[-] {name} installation aborted or already exists, exit code: {process.ExitCode}"));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(Language.L($"[-] 无法调用 winget: {ex.Message}", $"[-] Unable to call winget: {ex.Message}"));
                Console.WriteLine(Language.L("    请确保你的系统为 Windows 10/11，且应用商店工作正常。", "    Make sure your system is Windows 10/11 and the App Store is working."));
            }
        }

        static bool SetupAdbForward(int port)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "adb",
                    Arguments = $"forward tcp:{port} tcp:{port}",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                process.WaitForExit();

                if (process.ExitCode == 0) return true;

                string err = process.StandardError.ReadToEnd();
                Console.WriteLine(Language.L($"[-] ADB 转发失败: {err}", $"[-] ADB forward failed: {err}"));
                if (err.Contains("not found") || err.Contains("无法识别"))
                {
                    Console.WriteLine(Language.L("[-] 系统未找到 adb 命令！请返回主菜单选择 [3] 一键安装环境。", "[-] ADB command not found! Please return to main menu and select [3] Install Environment."));
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(Language.L($"[-] 无法执行 adb 命令。请返回主菜单选择 [3] 安装环境。详细异常: {ex.Message}", $"[-] Unable to execute ADB command. Return to main menu and select [3]. Details: {ex.Message}"));
                return false;
            }
        }
    }
}