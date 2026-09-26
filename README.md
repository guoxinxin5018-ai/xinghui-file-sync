# 星汇文件同步助手

这是可直接迁移到新电脑的完整项目包。

## 直接使用

双击根目录中的 `星汇-文件同步助手.exe` 即可运行，无需安装。

## 修改并重新生成

1. 使用 Visual Studio、VS Code 或其他文本编辑器打开 `FolderSyncAssistant.cs`。
2. 修改完成后双击 `生成软件.bat`。
3. 新软件会生成到 `dist\星汇-文件同步助手.exe`。

项目使用 Windows 自带的 .NET Framework 4.x 编译器，不依赖第三方 NuGet 包。

## 项目文件

- `FolderSyncAssistant.cs`：完整程序源码。
- `assets\星汇.ico`：程序和托盘图标。
- `星汇.manifest`：Windows 高 DPI 显示清单。
- `生成软件.bat`：一键编译脚本。
- `星汇-文件同步助手.exe`：已经编译好的单文件程序。

任务配置不会写入项目目录，而是保存在当前 Windows 用户的 `%APPDATA%\星汇\tasks.xml`。换电脑后可重新创建任务，也可自行复制这个配置文件。
