# 🖕 FuckAndroidMTP

**告别垃圾 MTP，把安卓手机变成 Windows 上的真正的物理级映射磁盘！** *Say goodbye to trash MTP. Turn your Android phone into a REAL physical-level mapped drive on Windows!*

## 😤 为什么会有这个项目？ (Why does this exist?)

天下苦安卓 MTP（媒体传输协议）久矣！
传输大文件经常卡死？算不出剩余时间？无法直接在手机里编辑 Word 文档？系统隐藏文件疯狂错乱？

**FuckAndroidMTP** 是一个基于 **Dokan.NET** 和 **Android 底层 I/O** 打造的跨平台虚拟文件系统（VFS）。它彻底绕过了 MTP 协议，通过 USB (ADB 端口转发) 或 Wi-Fi (局域网 TCP) 建立高速底层隧道，将你的安卓手机直接挂载为 Windows 的一个原生盘符（如 `Z:\`）。

**一句话总结：像插拔真正的 U 盘一样使用你的安卓手机！**

## ✨ 核心特性 (Features)

* 🚀 **真·物理映射 (True Native Drive):** 完美伪装成 Windows 本地 NTFS 磁盘。你可以直接在里面用 VS Code 写代码、用 Word 编辑文档、用播放器看电影，**无需来回复制粘贴**。
* 🔌 **双模直连 (Dual Connection Modes):**
* **USB 极速模式:** 自动配置 ADB 端口转发，享受媲美本地磁盘的极低延迟。
* **Wi-Fi 无线模式:** 处于同一局域网下输入 IP 即可隔空挂载。


* 🛡️ **双轨权限架构 (Dual Storage Modes on Android):**
* **U盘沙盒模式:** 挂载 App 私有目录。**免任何动态权限**，极致读写性能。
* **全局文件管理模式:** 直接挂载 `/sdcard/` 根目录。适配 Android 11+ `MANAGE_EXTERNAL_STORAGE` 权限。


* 📦 **傻瓜式一键环境部署 (1-Click Environment Setup):** C# 控制台内置 `winget` 自动化脚本，一键静默安装 ADB 工具箱和 Dokan 驱动，自动配置系统环境变量。
* 📊 **1:1 原机细节还原 (Perfect Detail Sync):** 动态同步手机真实的存储容量、系统级隐藏/只读属性，并在 Windows 盘符霸气展示你的手机真实型号。
* 🌍 **双语支持 (Bilingual CLI):** 内置中/英双语无缝切换。

## 🏗️ 架构原理 (Architecture)

```text
[ Windows 资源管理器 ]
        ↕
[ Dokan.NET (虚拟磁盘驱动) ] 
        ↕ (C# 代理层，拦截底层 Read/Write/Create 请求)
[ USB (ADB Forward) / Wi-Fi (TCP 8080) ]
        ↕ (自定义大端序 64KB 分块二进制传输协议)
[ Android 前台保活服务 (Foreground Service + WakeLock) ]
        ↕ (RandomAccessFile 底层 I/O)
[ Android 本地闪存 (ROM) ]

```

## 🛠️ 快速开始 (Quick Start)

### 1. 准备条件 (Prerequisites)

* **PC端:** Windows 10 / Windows 11 (x64)
* **手机端:** Android 8.0 及以上 (需开启“开发者选项”及“USB 调试”)

### 2. 运行步骤 (Usage)

1. **安装安卓端 App:** 编译并安装本项目中的 Android App。
2. **启动手机服务:** 打开 App，选择【U盘沙盒模式】或【全局文件管理模式】，启动服务。
3. **连接电脑:** 使用数据线连接手机与电脑，或确保它们在同一个 Wi-Fi 网络下。
4. **运行 Windows 客户端:** 运行 `FuckAndroidMTP.exe`。
* *初次使用请按 `[3]` 一键安装 Dokan 和 ADB 依赖环境（安装完 Dokan 后需重启电脑生效）。*


5. **选择模式并挂载:** 选择 `[1]` USB 模式 或 `[2]` Wi-Fi 模式。
6. **享受降维打击:** 打开“我的电脑”，你的 `FuckAndroidMTP (Z:)` 已经就绪！

## 💡 常见问题 (FAQ)

**Q: 复制几 GB 的大电影会不会内存溢出 (OOM) 崩溃？** A: 绝对不会！底层采用了 64KB 的 Chunked I/O 流式分块读写算法，无论传输多大的文件，安卓和 PC 端的内存占用永远锁定在几 MB。

**Q: 启动 C# 客户端直接闪退报错？** A: 请确保你的网络正常且 Windows 商店组件 (winget) 可用。项目使用了 JIT 隔离技术，即便你没有安装 Dokan.dll，程序也会正常启动并引导你进入 `[3]` 进行一键下载安装。

**Q: 拔掉线或者关闭手机 App，电脑会卡死吗？** A: 不会。我们加入了极其完善的网络异常捕获机制与断开事件通知。连接一旦断开，盘符会自动安全卸载，资源管理器不会出现“死亡小圆圈”。

## 📜 开源协议 (License)

本项目采用 [MIT License](https://www.google.com/search?q=https://opensource.org/licenses/MIT) 开源。您可以自由地使用、修改和分发，但请保留原作者声明。

---

*If this project saves you from MTP hell, consider giving it a ⭐!*

---

这篇 README 帮你把项目的硬核痛点、技术深度、以及保姆级的防呆设计全部清晰地列出来了。直接推送到你的 GitHub，绝对能吸引不少同样痛恨 MTP 的开发者的目光！
