# 🖕 FuckAndroidMTP

[🇨🇳 简体中文 (Chinese)](CN.md) | **🇺🇸 English**

**Say goodbye to trash MTP. Turn your Android phone into a REAL physical-level mapped drive on Windows!**

## 😤 Why does this exist?

We have suffered under Android's MTP (Media Transfer Protocol) for far too long!
Large file transfers freezing? Inaccurate time estimates? Unable to edit a Word document directly on your phone? System hidden files getting messed up?

**FuckAndroidMTP** is a cross-platform Virtual File System (VFS) built on **Dokan.NET** and **Android Native I/O**. It completely bypasses the MTP protocol by establishing a high-speed, low-level tunnel via USB (ADB Port Forwarding) or Wi-Fi (LAN TCP). It mounts your Android phone directly as a native Windows drive letter (e.g., `Z:\`).

**TL;DR: Use your Android phone exactly like a real, physical USB flash drive!**

## ✨ Core Features

* 🚀 **True Native Drive:** Perfectly disguised as a local Windows NTFS drive. You can write code in VS Code, edit Word documents, or watch movies directly from the drive—**no tedious copy-pasting required**.
* 🔌 **Dual Connection Modes:**
* **USB Speed Mode:** Automatically configures an ADB port forwarding tunnel for ultra-low latency that rivals local disks.
* **Wi-Fi Wireless Mode:** Mount your phone over the air by simply entering its IP address on the same LAN.


* 🛡️ **Dual Storage Modes (Android):**
* **Sandbox Mode:** Mounts the App's private directory. **Requires ZERO dynamic permissions** and delivers maximum I/O performance.
* **Global File Manager Mode:** Mounts the `/sdcard/` root directory. Fully adapted for Android 11+ `MANAGE_EXTERNAL_STORAGE` permissions.


* 📦 **1-Click Environment Setup:** The C# console features an integrated `winget` automation script to silently install the ADB Toolkit and Dokan drivers, configuring environment variables automatically.
* 📊 **1:1 Detail Synchronization:** Dynamically syncs your phone's real storage capacity, system-level hidden/read-only file attributes, and aggressively displays your phone's actual hardware model as the drive label.
* 🌍 **Bilingual CLI:** Built-in seamless switching between English and Chinese interfaces.

## 🏗️ Architecture

```text
[ Windows File Explorer ]
        ↕
[ Dokan.NET (Virtual Disk Driver) ] 
        ↕ (C# Proxy: Intercepts low-level Read/Write/Create requests)
[ USB (ADB Forward) / Wi-Fi (TCP 8080) ]
        ↕ (Custom Big-Endian 64KB Chunked Binary Protocol)
[ Android Foreground Service (+ WakeLock) ]
        ↕ (RandomAccessFile Native I/O)
[ Android Local Storage (ROM) ]

```

## 🛠️ Quick Start

### 1. Prerequisites

* **PC:** Windows 10 / Windows 11 (x64)
* **Phone:** Android 8.0 or higher (Developer Options & "USB Debugging" must be enabled)

### 2. Usage Guide

1. **Install the Android App:** Compile and install the Android APK from this project.
2. **Start the Phone Service:** Open the App, select either [Sandbox Mode] or [Global File Manager Mode], and start the service.
3. **Connect to PC:** Connect your phone via a USB cable, or ensure both devices are on the same Wi-Fi network.
4. **Run the Windows Client:** Launch `FuckAndroidMTP.exe`.
* *First-time users: Press `[3]` to 1-Click install the Dokan and ADB dependencies (Restart your PC after installing Dokan).*


5. **Select Mode & Mount:** Choose `[1]` for USB Mode or `[2]` for Wi-Fi Mode.
6. **Enjoy:** Open "My Computer" (This PC), and your `FuckAndroidMTP (Z:)` drive is ready to smash MTP!

## 💡 FAQ

**Q: Will copying a multi-gigabyte movie cause an Out-Of-Memory (OOM) crash on the phone?** A: Absolutely not! The underlying engine uses a 64KB Chunked I/O streaming algorithm. No matter how large the file is, memory usage on both Android and PC remains locked at just a few megabytes.

**Q: The C# client crashes immediately upon startup?** A: Ensure your internet connection is active and Windows Store components (`winget`) are functional. The project uses JIT isolation technology; even if you lack `Dokan.dll`, the program will launch safely and guide you to option `[3]` for a 1-click installation.

**Q: Will my PC freeze if I unplug the cable or force-close the Android app?** A: No. We have implemented robust network exception catching and disconnect event notifications. The moment the connection drops, the drive safely unmounts itself. Windows Explorer will simply back out, preventing the dreaded "infinite loading circle."

## 📜 License

This project is licensed under the [MIT License](https://www.google.com/search?q=https://opensource.org/licenses/MIT). You are free to use, modify, and distribute it, but please retain the original author attribution.

---

*If this project saves you from MTP hell, consider giving it a ⭐!*
