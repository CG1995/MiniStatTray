# MiniStatTray

简体中文 | [English](README.md)

MiniStatTray 是一个很小的 Windows 通知区域监控工具，用来显示 CPU 负载、GPU 实时功耗、内存占用和网络上传/下载速度。

它的目标是轻量、直接、少打扰：不安装驱动，不做复杂配置，只在任务栏通知区域显示几个小图标。

## 显示内容

程序会显示 5 个通知区域图标，固定顺序为：

1. CPU 使用率
2. GPU 功耗，单位 W
3. 内存使用率
4. 下载速度
5. 上传速度

鼠标悬停在图标上可以看到完整指标名称和数值。

## 下载和运行

可直接运行的文件在：

`dist/MiniStatTray.exe`

双击运行即可，不需要安装。

## 构建

在已安装 .NET Framework 的 Windows 上运行：

```powershell
.\build.ps1
```

构建结果会输出到：

`dist/MiniStatTray.exe`

## 说明

- CPU、内存和网络数据来自 Windows API。
- NVIDIA GPU 功耗通过 NVML 读取；如果系统没有 NVIDIA/NVML，GPU 功耗会显示为不可用。
- 程序不安装驱动。
- 正常运行不需要管理员权限。
- Windows 可能会把新通知图标放到隐藏区域里，可以手动拖到任务栏上固定显示。
