using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Text;
using DokanNet;
using FileAccess = DokanNet.FileAccess;

namespace AndroidVirtualDrive
{
    public class DokanAndroidDrive : IDokanOperations, IDisposable
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _ioLock = new object();

        private string _deviceModel = "Android Device";

        // 新增：断开连接事件与标志位
        public event Action OnDisconnected;
        private volatile bool _isDisconnected = false;

        public void Connect(string ip, int port)
        {
            _client = new TcpClient();
            _client.NoDelay = true;

            // 【新增】：客户端同步扩大 TCP 窗口为 4MB
            _client.ReceiveBufferSize = 4 * 1024 * 1024;
            _client.SendBufferSize = 4 * 1024 * 1024;

            _client.Connect(ip, port);
            _stream = _client.GetStream();

            Console.WriteLine(Language.L($"[+] 成功连接到安卓端 {ip}:{port}",$"[+] Connected Android device {ip}:{port}"));
            // 刚连上就发送指令 10 获取手机型号
            try
            {
                SendCommandHeader(10, "");
                int len = ReadBigEndianInt();
                byte[] modelBytes = new byte[len];
                ReadFully(modelBytes);
                _deviceModel = Encoding.UTF8.GetString(modelBytes);
                Console.WriteLine(Language.L($"[+] 识别到终端设备: {_deviceModel}", $"[+] Finded device name: {_deviceModel}"));
            }
            catch (Exception)
            {
                // 兼容老版本安卓端未实现指令 10 的情况
                Console.WriteLine(Language.L("[-] 无法获取设备型号，使用默认名称。", "Unable get device name , use normal name"));
            }
        }

        private void TriggerDisconnect()
        {
            if (_isDisconnected) return;
            _isDisconnected = true;
            OnDisconnected?.Invoke(); // 通知主程序断开
        }

        // ==========================================
        // 6. 补充缺失的接口 (修复 CS0535 报错)
        // ==========================================

        // Windows 请求将缓存强制刷新到物理磁盘
        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            // 我们的 WriteFile 逻辑是同步的，只有在收到安卓端返回的 ACK=1 后才会告诉 Windows 写入成功。
            // 因此，到达这里时，数据实际上已经安全落在安卓手机里了。直接返回成功即可。
            return DokanResult.Success;
        }

        // Windows 请求按通配符搜索文件 (例如搜索 *.jpg)
        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();

            // 【核心偷懒技巧】：直接返回 NotImplemented！
            // Dokan 底层非常智能。当它发现你没有自己实现正则匹配搜索时，
            // 它会自动降级去调用我们已经写好的 FindFiles 获取该目录下所有文件，
            // 然后由 Windows 内核自己去完成 *.jpg 的字符串匹配过滤。这样我们就不用自己手写正则了。
            return DokanResult.NotImplemented;
        }

        // ==========================================
        // 核心网络通信协议方法
        // ==========================================
        private void SendCommandHeader(byte cmd, string path)
        {
            _stream.WriteByte(cmd);
            byte[] pathBytes = Encoding.UTF8.GetBytes(path);
            WriteBigEndian(pathBytes.Length);
            _stream.Write(pathBytes, 0, pathBytes.Length);
        }

        private void WriteBigEndian(int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            _stream.Write(bytes, 0, bytes.Length);
        }

        private void WriteBigEndian(long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            _stream.Write(bytes, 0, bytes.Length);
        }

        private int ReadBigEndianInt()
        {
            byte[] buffer = new byte[4];
            ReadFully(buffer);
            if (BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return BitConverter.ToInt32(buffer, 0);
        }

        private long ReadBigEndianLong()
        {
            byte[] buffer = new byte[8];
            ReadFully(buffer);
            if (BitConverter.IsLittleEndian) Array.Reverse(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }

        private void ReadFully(byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = _stream.Read(buffer, total, buffer.Length - total);
                if (read == 0) throw new EndOfStreamException("网络连接异常断开");
                total += read;
            }
        }

        // ==========================================
        // 1. 文件/目录基础信息获取
        // ==========================================
        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            fileInfo = default;
            // 【核心修复】：拦截根目录请求，直接伪造一个永远存在的文件夹信息
            if (fileName == "\\")
            {
                fileInfo = new FileInformation
                {
                    FileName = fileName,
                    Attributes = FileAttributes.Directory,
                    CreationTime = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    LastWriteTime = DateTime.Now,
                    Length = 0
                };
                return DokanResult.Success;
            }
            // 新增：如果已经断开，直接告诉 Windows 设备不可用
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(1, fileName);
                    bool exists = _stream.ReadByte() == 1;

                    if (!exists)
                    {
                        fileInfo = default;
                        return DokanResult.FileNotFound;
                    }

                    int flags = _stream.ReadByte();
                    bool isDir = (flags & 1) != 0;
                    bool isHidden = (flags & 2) != 0;
                    bool isReadOnly = (flags & 4) != 0;

                    FileAttributes attrs = 0;
                    if (isDir) attrs |= FileAttributes.Directory;
                    if (isHidden) attrs |= FileAttributes.Hidden;
                    if (isReadOnly) attrs |= FileAttributes.ReadOnly;
                    if (attrs == 0) attrs = FileAttributes.Normal;

                    long size = ReadBigEndianLong();
                    long lastModified = ReadBigEndianLong();

                    fileInfo = new FileInformation
                    {
                        FileName = fileName,
                        Attributes = attrs,
                        CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(lastModified).DateTime,
                        LastWriteTime = DateTimeOffset.FromUnixTimeMilliseconds(lastModified).DateTime,
                        LastAccessTime = DateTime.Now,
                        Length = size
                    };
                    return DokanResult.Success;
                }
            }
            catch (Exception) // 捕获 IOException, SocketException 等所有网络异常
            {
                TriggerDisconnect();
                return DokanResult.NotReady; // 返回此状态码，Windows 绝不会卡死
            }
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(2, fileName);
                    int fileCount = ReadBigEndianInt();

                    for (int i = 0; i < fileCount; i++)
                    {
                        int nameLen = ReadBigEndianInt();
                        byte[] nameBytes = new byte[nameLen];
                        ReadFully(nameBytes);
                        string name = Encoding.UTF8.GetString(nameBytes);

                        bool isDir = _stream.ReadByte() == 1;
                        long size = ReadBigEndianLong();
                        long lastMod = ReadBigEndianLong();

                        files.Add(new FileInformation
                        {
                            FileName = name,
                            Attributes = isDir ? FileAttributes.Directory : FileAttributes.Normal,
                            Length = size,
                            CreationTime = DateTimeOffset.FromUnixTimeMilliseconds(lastMod).DateTime,
                            LastWriteTime = DateTimeOffset.FromUnixTimeMilliseconds(lastMod).DateTime
                        });
                    }
                }
                return DokanResult.Success;
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        // ==========================================
        // 2. 文件读写核心
        // ==========================================
        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            bytesRead = 0;
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(3, fileName);
                    WriteBigEndian(offset);
                    WriteBigEndian(buffer.Length);

                    bytesRead = ReadBigEndianInt();
                    if (bytesRead > 0)
                    {
                        byte[] tempBuf = new byte[bytesRead];
                        ReadFully(tempBuf);
                        Array.Copy(tempBuf, buffer, bytesRead);
                    }
                    return DokanResult.Success;
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(4, fileName);
                    WriteBigEndian(offset);
                    WriteBigEndian(buffer.Length);
                    _stream.Write(buffer, 0, buffer.Length);

                    int ack = _stream.ReadByte();
                    bytesWritten = ack == 1 ? buffer.Length : 0;
                    return ack == 1 ? DokanResult.Success : DokanResult.Error;
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        // ==========================================
        // 3. 文件/目录的生命周期 (创建、关闭、清理)
        // ==========================================
        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            // 拦截根目录，直接放行
            if (fileName == "\\")
            {
                info.IsDirectory = true;
                return DokanResult.Success;
            }
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(1, fileName);
                    bool exists = _stream.ReadByte() == 1;

                    if (exists)
                    {
                        // --- 文件/目录已存在 ---

                        int flags = _stream.ReadByte();
                        bool isDir = (flags & 1) != 0;

                        ReadBigEndianLong();
                        ReadBigEndianLong();

                        if (isDir) info.IsDirectory = true;

                        if (info.IsDirectory && !isDir) return DokanResult.NotADirectory;
                        if (mode == FileMode.CreateNew) return DokanResult.FileExists;

                        // 【核心修复1】：如果 Windows 要求覆盖/清空已有文件 (比如保存编辑后的文本)
                        if (mode == FileMode.Create || mode == FileMode.Truncate)
                        {
                            SendCommandHeader(8, fileName); // 指令 8: SetEndOfFile (截断文件)
                            WriteBigEndian(0L);             // 大小设为 0
                            _stream.ReadByte();             // 等待 ACK
                        }

                        return DokanResult.Success;
                    }
                    else
                    {
                        // --- 文件/目录不存在 ---
                        if (mode == FileMode.Open || mode == FileMode.Truncate)
                            return DokanResult.FileNotFound;

                        // 要求新建目录
                        if (info.IsDirectory)
                        {
                            SendCommandHeader(5, fileName);
                            return _stream.ReadByte() == 1 ? DokanResult.Success : DokanResult.Error;
                        }

                        // 【核心修复2】：要求新建文件！必须立刻在安卓端创建一个物理空文件
                        // 我们通过复用 WriteFile(指令4)，发送 0 字节来实现创建
                        SendCommandHeader(4, fileName);
                        WriteBigEndian(0L); // 偏移量 0
                        WriteBigEndian(0);  // 写入长度 0

                        int ack = _stream.ReadByte();
                        return ack == 1 ? DokanResult.Success : DokanResult.Error;
                    }
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
}

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            if (_isDisconnected) 
                return;
            try
            {
                // 当文件的最后一个句柄被关闭时触发。如果标记了 DeleteOnClose，则执行物理删除。
                if (info.DeletePending)
                {
                    lock (_ioLock)
                    {
                        SendCommandHeader(6, fileName); // 指令 6: 删除文件/目录
                        _stream.ReadByte(); // 等待 ACK
                    }
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
            }
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            // 句柄关闭，释放本地资源，网络存储无需处理
        }

        // ==========================================
        // 4. 重命名、删除与属性修改
        // ==========================================
        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            // Dokan 规范：这里只需返回 Success，真正的删除由上面的 Cleanup(DeleteOnClose=true) 执行。
            return DokanResult.Success;
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(7, oldName); // 指令 7: 移动/重命名

                    // 接着发送 newName 的字符串
                    byte[] newNameBytes = Encoding.UTF8.GetBytes(newName);
                    WriteBigEndian(newNameBytes.Length);
                    _stream.Write(newNameBytes, 0, newNameBytes.Length);

                    return _stream.ReadByte() == 1 ? DokanResult.Success : DokanResult.AccessDenied;
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    SendCommandHeader(8, fileName); // 指令 8: 修改文件大小 (截断)
                    WriteBigEndian(length);
                    return _stream.ReadByte() == 1 ? DokanResult.Success : DokanResult.Error;
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            return SetEndOfFile(fileName, length, info);
        }

        // ==========================================
        // 5. 磁盘总览与未实现的不重要接口
        // ==========================================
        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            freeBytesAvailable = 0;
            totalNumberOfBytes = 0;
            totalNumberOfFreeBytes = 0;
            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                lock (_ioLock)
                {
                    try
                    {
                        // 发送指令 9 给安卓，路径传根目录 "\\" 即可
                        SendCommandHeader(9, "\\");

                        // 读取安卓端返回的两个 8 字节 long 值
                        totalNumberOfBytes = ReadBigEndianLong();
                        freeBytesAvailable = ReadBigEndianLong();

                        // Dokan 要求的第三个参数通常与可用空间相同
                        totalNumberOfFreeBytes = freeBytesAvailable;

                        return DokanResult.Success;
                    }
                    catch (Exception)
                    {
                        // 如果在这极其短暂的瞬间网络断开，安全地返回 0
                        freeBytesAvailable = 0;
                        totalNumberOfBytes = 0;
                        totalNumberOfFreeBytes = 0;
                        return DokanResult.Error;
                    }
                }
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            volumeLabel = $"{_deviceModel}";
            fileSystemName = "NTFS"; // 伪装成 NTFS 以获得最大的 Windows 兼容性
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.UnicodeOnDisk;
            maximumComponentLength = 255;

            if (_isDisconnected) return DokanResult.NotReady;
            try
            {
                volumeLabel = $"{_deviceModel}";
                fileSystemName = "NTFS"; // 伪装成 NTFS 以获得最大的 Windows 兼容性
                features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.UnicodeOnDisk;
                maximumComponentLength = 255;
                return DokanResult.Success;
            }
            catch (Exception)
            {
                TriggerDisconnect();
                return DokanResult.NotReady;
            }
        }

        // NTFS 安全性、流、文件时间修改在安卓沙盒中很难完美对应，直接返回成功或不支持即可，不影响核心使用。
        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info) => DokanResult.Success;
        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info) => DokanResult.Success;
        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info) => DokanResult.Success;
        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info) => DokanResult.Success;
        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info) { security = null; return DokanResult.NotImplemented; }
        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info) => DokanResult.NotImplemented;
        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info) { streams = new List<FileInformation>(); return DokanResult.NotImplemented; }
        public NtStatus Mounted(string mountPoint, IDokanFileInfo info) => DokanResult.Success;
        public NtStatus Unmounted(IDokanFileInfo info) => DokanResult.Success;

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}
