package com.xlzhen.virtualdrive

import android.app.*
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat
import java.io.File

class FileServerService : Service() {

    private val CHANNEL_ID = "VirtualDriveChannel"
    private var wakeLock: PowerManager.WakeLock? = null
    private var fileServer: FileServer? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        acquireWakeLock()

    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        // 接收从 Activity 传过来的目标挂载目录，如果没有传，默认使用沙盒目录
        val targetPath = intent?.getStringExtra("BASE_PATH") ?: getExternalFilesDir("VirtualDrive")!!.absolutePath

        // 如果服务已经运行，先停掉旧的
        fileServer?.stop()

        // 启动新的挂载点
        fileServer = FileServer(File(targetPath), 8080)
        fileServer?.start()

        val notification = NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle("FuckAndroidMTP Running")
            .setContentText("Mount Dir: $targetPath")
            .setSmallIcon(android.R.drawable.ic_menu_save)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .build()

        startForeground(1, notification)
        return START_STICKY
    }

    override fun onDestroy() {
        super.onDestroy()
        fileServer?.stop()
        wakeLock?.let {
            if (it.isHeld) it.release()
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun acquireWakeLock() {
        val powerManager = getSystemService(Context.POWER_SERVICE) as PowerManager
        wakeLock = powerManager.newWakeLock(
            PowerManager.PARTIAL_WAKE_LOCK,
            "VirtualDrive::FileServerWakeLock"
        )
        wakeLock?.acquire()
    }

    private fun createNotificationChannel() {
        val channel = NotificationChannel(
            CHANNEL_ID,
            "Virtual Disk",
            NotificationManager.IMPORTANCE_LOW
        )
        val manager = getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(channel)
    }
}