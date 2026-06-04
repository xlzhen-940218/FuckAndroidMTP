package com.xlzhen.virtualdrive

import java.io.*
import java.net.ServerSocket
import java.net.SocketException

class FileServer(private val baseDir: File, private val port: Int = 8080) {

    private var serverSocket: ServerSocket? = null
    @Volatile
    private var isRunning = false

    fun start() {
        if (!baseDir.exists()) baseDir.mkdirs()
        isRunning = true
        Thread {
            try {
                serverSocket = ServerSocket(port)
                println("[+] 安卓文件服务端已启动，监听端口: $port")

                while (isRunning) {
                    try {
                        val client = serverSocket!!.accept()
                        client.tcpNoDelay = true // 禁用 Nagle 算法，降低 I/O 延迟
                        handleClient(client)
                    } catch (e: SocketException) {
                        if (!isRunning) break
                    } catch (e: Exception) {
                        e.printStackTrace()
                    }
                }
            } catch (e: Exception) {
                e.printStackTrace()
            }
        }.start()
    }

    fun stop() {
        isRunning = false
        serverSocket?.close()
    }

    private fun handleClient(client: java.net.Socket) {
        Thread {
            try {
                val input = DataInputStream(BufferedInputStream(client.getInputStream()))
                val output = DataOutputStream(BufferedOutputStream(client.getOutputStream()))

                while (isRunning) {
                    val cmd = input.readByte().toInt()

                    // 收到 C# 发来的路径字节
                    val pathLen = input.readInt()
                    val pathBytes = ByteArray(pathLen)
                    input.readFully(pathBytes)

                    // 【核心修复1】：将 Windows 路径 (\) 转换为安卓本地路径，并剔除开头的斜杠
                    val relativePath = String(pathBytes, Charsets.UTF_8).replace("\\", "/").trimStart('/')

                    // 如果路径为空（说明访问的是根目录 Z:\），直接指向 baseDir
                    val targetFile = if (relativePath.isEmpty()) baseDir else File(baseDir, relativePath)

                    when (cmd) {
                        1 -> { // GetFileInfo (修复了并发安全问题)
                            val exists = targetFile.exists()
                            output.writeByte(if (exists) 1 else 0)
                            if (exists) {
                                var flags = 0
                                if (targetFile.isDirectory) flags = flags or 1  // Bit 0: 文件夹
                                if (targetFile.isHidden) flags = flags or 2     // Bit 1: 隐藏文件
                                if (!targetFile.canWrite()) flags = flags or 4  // Bit 2: 只读文件
                                output.writeByte(flags)
                                output.writeLong(targetFile.length())
                                output.writeLong(targetFile.lastModified())
                            }
                            output.flush()
                        }
                        2 -> { // FindFiles
                            val files = targetFile.listFiles() ?: emptyArray()
                            output.writeInt(files.size)
                            for (f in files) {
                                val nameBytes = f.name.toByteArray(Charsets.UTF_8)
                                output.writeInt(nameBytes.size)
                                output.write(nameBytes)
                                var flags = 0
                                if (f.isDirectory) flags = flags or 1  // Bit 0: 文件夹
                                if (f.isHidden) flags = flags or 2     // Bit 1: 隐藏文件
                                if (!f.canWrite()) flags = flags or 4  // Bit 2: 只读文件
                                output.writeByte(flags)
                                output.writeLong(f.length())
                                output.writeLong(f.lastModified())
                            }
                            output.flush()
                        }
                        3 -> { // ReadFile (引入 64KB 分块读取，防止 OOM)
                            val offset = input.readLong()
                            val length = input.readInt()

                            if (targetFile.exists() && targetFile.isFile) {
                                RandomAccessFile(targetFile, "r").use { raf ->
                                    raf.seek(offset)
                                    val available = raf.length() - offset
                                    val actualRead = if (available < 0) 0 else if (available > length) length else available.toInt()

                                    output.writeInt(actualRead)
                                    if (actualRead > 0) {
                                        val chunkBuffer = ByteArray(65536) // 固定的 64KB 内存缓存
                                        var remaining = actualRead
                                        while (remaining > 0) {
                                            val toRead = if (remaining > chunkBuffer.size) chunkBuffer.size else remaining
                                            val readCount = raf.read(chunkBuffer, 0, toRead)
                                            if (readCount <= 0) break
                                            output.write(chunkBuffer, 0, readCount)
                                            remaining -= readCount
                                        }
                                    }
                                }
                            } else {
                                output.writeInt(0) // 文件不存在或不可读
                            }
                            output.flush()
                        }
                        4 -> { // WriteFile (引入 64KB 分块写入，彻底解决 1.16GB 分配崩溃)
                            val offset = input.readLong()
                            val length = input.readInt()

                            targetFile.parentFile?.let { if (!it.exists()) it.mkdirs() }
                            if (!targetFile.exists()) targetFile.createNewFile()

                            RandomAccessFile(targetFile, "rw").use { raf ->
                                raf.seek(offset)
                                val chunkBuffer = ByteArray(65536) // 固定的 64KB 内存缓存
                                var remaining = length
                                while (remaining > 0) {
                                    val toRead = if (remaining > chunkBuffer.size) chunkBuffer.size else remaining
                                    input.readFully(chunkBuffer, 0, toRead)
                                    raf.write(chunkBuffer, 0, toRead)
                                    remaining -= toRead
                                }
                            }
                            output.writeByte(1) // 返回成功 ACK
                            output.flush()
                        }
                        5 -> { // CreateDirectory (新建文件夹)
                            val success = targetFile.mkdirs()
                            output.writeByte(if (success || targetFile.exists()) 1 else 0)
                            output.flush()
                        }
                        6 -> { // Delete File/Directory (删除文件或文件夹)
                            val success = if (targetFile.isDirectory) {
                                targetFile.deleteRecursively() // 删除文件夹及里面所有内容
                            } else {
                                targetFile.delete()
                            }
                            output.writeByte(if (success || !targetFile.exists()) 1 else 0)
                            output.flush()
                        }
                        7 -> { // Move/Rename (重命名或移动)
                            val newPathLen = input.readInt()
                            val newPathBytes = ByteArray(newPathLen)
                            input.readFully(newPathBytes)
                            val newRelativePath = String(newPathBytes, Charsets.UTF_8).replace("\\", "/")
                            val newTargetFile = File(baseDir, newRelativePath)

                            newTargetFile.parentFile?.mkdirs()
                            val success = targetFile.renameTo(newTargetFile)
                            output.writeByte(if (success) 1 else 0)
                            output.flush()
                        }
                        8 -> { // SetEndOfFile (截断/修改文件大小)
                            val newLength = input.readLong()
                            if (targetFile.exists() && targetFile.isFile) {
                                RandomAccessFile(targetFile, "rw").use { raf ->
                                    raf.setLength(newLength)
                                }
                                output.writeByte(1)
                            } else {
                                output.writeByte(0)
                            }
                            output.flush()
                        }
                        9 -> { // GetDiskFreeSpace (获取手机真实磁盘容量)
                            // baseDir 是我们当前挂载的根目录，通过它可以获取所在存储分区的总容量和可用容量
                            val totalSpace = baseDir.totalSpace
                            val freeSpace = baseDir.usableSpace

                            // 按大端序发送两个 Long 给 C# (各 8 字节)
                            output.writeLong(totalSpace)
                            output.writeLong(freeSpace)
                            output.flush()
                        }
                        10 -> { // 获取安卓设备硬件型号
                            val modelBytes = android.os.Build.MODEL.toByteArray(Charsets.UTF_8)
                            output.writeInt(modelBytes.size)
                            output.write(modelBytes)
                            output.flush()
                        }
                    }
                }
            } catch (e: Exception) {
                println("[-] 客户端断开连接")
            } finally {
                client.close()
            }
        }.start()
    }
}