package com.xlzhen.virtualdrive

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.os.Environment
import android.provider.Settings
import android.widget.Button
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

class MainActivity : AppCompatActivity() {

    // 定义语言枚举和当前语言变量
    enum class Lang { ZH, EN, BILINGUAL }
    private var currentLang = Lang.ZH

    private lateinit var statusText: TextView
    private lateinit var pathText: TextView
    private lateinit var layout: LinearLayout

    // 用于记录等待启动前台服务的目录路径，以便在权限回调中继续启动
    private var pendingPath: String? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        // 先初始化基础容器，暂时不添加子控件
        layout = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(50, 50, 50, 50)
        }
        setContentView(layout)

        // 启动时弹出语言选择框
        showLanguageSelector()
    }

    // 语言选择弹窗
    private fun showLanguageSelector() {
        val options = arrayOf("中文 (Chinese)", "English", "中英双语 (Bilingual)")
        AlertDialog.Builder(this)
            .setTitle("Please select language / 请选择语言")
            .setItems(options) { _, which ->
                currentLang = when (which) {
                    0 -> Lang.ZH
                    1 -> Lang.EN
                    else -> Lang.BILINGUAL
                }
                // 语言选择完毕后，初始化并渲染 UI
                initUi()
            }
            .setCancelable(false) // 强制必须选择
            .show()
    }

    // 多语言输出辅助方法
    private fun L(zh: String, en: String): String {
        return when (currentLang) {
            Lang.ZH -> zh
            Lang.EN -> en
            Lang.BILINGUAL -> "$zh\n$en"
        }
    }

    // 核心 UI 初始化（在语言选择后调用）
    private fun initUi() {
        statusText = TextView(this).apply {
            text = L("服务未启动", "Service Not Started")
            textSize = 20f
            setPadding(0, 0, 0, 30)
        }

        pathText = TextView(this).apply {
            text = L("等待选择挂载模式...", "Waiting to select mount mode...")
            setPadding(0, 0, 0, 50)
        }

        // 按钮 1：应用沙盒模式 (免权限)
        val btnSandbox = Button(this).apply {
            text = L("启动：U盘沙盒模式 (免权限/极速)", "Start: USB Sandbox Mode (No Auth/Fast)")
            setOnClickListener {
                val dir = getExternalFilesDir("VirtualDrive")!!.absolutePath
                // 替换原来的 startDriveService(dir)
                checkNotificationAndStartService(dir)
            }
        }

        // 按钮 2：全局文件模式 (需授权)
        val btnGlobal = Button(this).apply {
            text = L("启动：全局文件管理模式 (/sdcard)", "Start: Global File Management Mode (/sdcard)")
            setOnClickListener {
                checkAndRequestGlobalStoragePermission {
                    val dir = Environment.getExternalStorageDirectory().absolutePath
                    // 替换原来的 startDriveService(dir)
                    checkNotificationAndStartService(dir)
                }
            }
        }

        // 停止服务按钮
        val btnStop = Button(this).apply {
            text = L("停止服务", "Stop Service")
            setOnClickListener {
                stopService(Intent(this@MainActivity, FileServerService::class.java))
                statusText.text = L("服务已停止", "Service Stopped")
                pathText.text = ""
            }
        }

        // 将控件添加到布局
        layout.addView(statusText)
        layout.addView(pathText)
        layout.addView(btnSandbox)
        layout.addView(btnGlobal)
        layout.addView(btnStop)
    }

    // 检查 Android 13 通知权限，然后再启动服务
    private fun checkNotificationAndStartService(path: String) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED) {
                // 记录路径，申请权限
                pendingPath = path
                ActivityCompat.requestPermissions(
                    this,
                    arrayOf(Manifest.permission.POST_NOTIFICATIONS),
                    101 // 通知权限的 RequestCode
                )
                return
            }
        }
        // 如果已经有通知权限，或者系统低于 Android 13，直接启动
        startDriveService(path)
    }

    // 统一处理系统权限请求回调
    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)

        if (requestCode == 101) {
            // Android 13 通知权限回调
            // 无论用户是否同意发送通知，我们都继续启动服务。如果拒绝了，服务照常在后台跑，只是通知栏没常驻图标。
            pendingPath?.let {
                startDriveService(it)
                pendingPath = null
            }
        } else if (requestCode == 100) {
            // Android 8-10 存储权限回调
            if (grantResults.isNotEmpty() && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                val dir = Environment.getExternalStorageDirectory().absolutePath
                checkNotificationAndStartService(dir)
            } else {
                Toast.makeText(this, L("未授予存储权限", "Storage permission denied"), Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun startDriveService(path: String) {
        val serviceIntent = Intent(this, FileServerService::class.java).apply {
            putExtra("BASE_PATH", path)
        }
        startForegroundService(serviceIntent)
        statusText.text = L("FuckAndroidMTP 服务运行中", "FuckAndroidMTP Service Running")
        pathText.text = L("当前映射目录:\n$path", "Current mapped directory:\n$path")
    }

    // 核心权限检查逻辑
    private fun checkAndRequestGlobalStoragePermission(onGranted: () -> Unit) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            // Android 11+
            if (Environment.isExternalStorageManager()) {
                onGranted()
            } else {
                Toast.makeText(
                    this,
                    L("请授权【所有文件访问权限】以访问根目录", "Please grant [All Files Access] to access root directory"),
                    Toast.LENGTH_LONG
                ).show()
                val intent = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION)
                val uri = Uri.fromParts("package", packageName, null)
                intent.data = uri
                startActivity(intent)
            }
        } else {
            // Android 8-10
            if (ContextCompat.checkSelfPermission(this, Manifest.permission.WRITE_EXTERNAL_STORAGE) == PackageManager.PERMISSION_GRANTED) {
                onGranted()
            } else {
                ActivityCompat.requestPermissions(
                    this,
                    arrayOf(Manifest.permission.WRITE_EXTERNAL_STORAGE, Manifest.permission.READ_EXTERNAL_STORAGE),
                    100
                )
            }
        }
    }
}