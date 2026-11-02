# MZip

一款面向 Windows 10/11 的压缩解压工具，支持 ZIP / 7Z 的创建与修改、RAR 的解压，内置 7-Zip 引擎，并深度集成资源管理器（默认解压程序、右键压缩/解压、统一文件图标）。

- 界面框架：WPF（.NET 8，代码后置，无 MVVM、无接口化）
- 压缩引擎：7-Zip 26.05 x64（LGPL，可商用免费）
- 日志：log4net
- 项目版本：v1.0.0

## 功能特性

### 压缩包操作
- 打开、浏览、测试 ZIP / 7Z / RAR / TAR / GZ / TGZ / BZ2 / XZ / ISO / CAB / JAR / APK / 分卷 001 等格式
- 解压全部、解压选中，支持密码重试、进度显示与取消
- 新建 ZIP / 7Z 压缩包，支持压缩级别、密码、7Z 加密文件名、ZIP AES-256
- 向 ZIP / 7Z 添加文件或删除条目（RAR 只读）
- 覆盖策略：每次询问 / 直接覆盖 / 跳过已有 / 自动重命名
- 列表全选（Ctrl+A）、复制选中条目（Ctrl+C）、粘贴到指定文件夹（Ctrl+V）
- 最近打开记录（最多 10 条）、拖拽打开、中文路径无乱码

### Windows 系统集成
- 设置中可注册为 `.zip` / `.rar` / `.7z` 默认解压程序（仅当前用户 HKCU，无需管理员权限）
- 一键跳转系统“默认应用”设置页
- 资源管理器右键菜单：
  - 文件 / 文件夹 / 多选 → “使用 MZip 压缩”，多选自动汇总为一个压缩包
  - `.zip` / `.rar` / `.7z` → “解压到本目录”（解压到同名子文件夹）、“解压到指定目录…”
- 注册后压缩文件统一显示 MZip 图标；取消关联时按备份恢复原图标

## 目录结构

```
MZip.slnx
├─ src/MZip/                 主程序
│  ├─ App.xaml(.cs)              启动与命令行解析
│  ├─ MainWindow.xaml(.cs)       主界面
│  ├─ Dialogs/                   创建、密码、覆盖策略、设置、关于
│  ├─ Models/                    实体类
│  ├─ Services/                  7-Zip 引擎、进程执行、设置、日志、系统集成
│  ├─ Assets/MZip.ico        应用图标
│  └─ tools/                     7z.exe / 7z.dll / License.txt
├─ installer/                    Inno Setup 安装脚本与简体中文语言文件
├─ scripts/build-installer.ps1   一键发布并编译安装包
├─ dist/                         安装包输出目录（构建生成）
├─ tools/                        7-Zip 原始文件
└─ docs/                         需求、设计、使用说明（按时间前缀分版本）
```

程序运行时在输出目录下生成：

- `config/settings.json`：明文 JSON 设置
- `logs/MZip<yyyy-MM-dd>.log`：按天滚动的日志

## 构建与运行

环境要求：Windows 10/11 x64、.NET 8 SDK（运行需 .NET 8 Desktop Runtime）。

```powershell
git clone <仓库地址>
cd 解压缩
dotnet build MZip.slnx -c Release
```

运行：

```powershell
src\MZip\bin\Release\net8.0-windows\MZip.exe
```

## 打包安装程序

环境要求：额外安装 [Inno Setup 6](https://jrsoftware.org/isdl.php)（免费可商用）；可执行 `winget install JRSoftware.InnoSetup` 安装。

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

脚本会读取 `MZip.csproj` 中的版本号，执行 win-x64 自包含发布到 `build\publish`，再用 Inno Setup 编译出 `dist\MZip-<版本>-Setup.exe`。

安装包特性：

- 默认安装到 `%LOCALAPPDATA%\Programs\MZip`，当前用户安装，无 UAC
- 自带 .NET 8 运行时，目标机无需预装
- 开始菜单快捷方式固定创建，桌面快捷方式可选
- 卸载保留 `config` 与 `logs` 用户数据
- 支持静默安装：`MZip-1.0.0-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /DIR="D:\Apps\MZip"`

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `MZip.exe "压缩包路径"` | 打开指定压缩包 |
| `MZip.exe --compress "路径1" ["路径2" …]` | 一键压缩为 7Z |
| `MZip.exe --extract-here "压缩包路径"` | 解压到压缩包同级同名文件夹 |
| `MZip.exe --extract-to "压缩包路径"` | 选择目录后解压 |

## 文档

- [需求规格说明书 v1.9](docs/202611021809_需求规格说明书_v1.9.md)
- [技术方案设计 v1.10](docs/202611021809_技术方案设计_v1.10.md)
- [使用说明 v1.9](docs/202611021809_使用说明_v1.9.md)
- 历史版本按“年月日时分 + 文档名 + 版本号”保存在 `docs/` 中

## 日志与问题反馈

- 日志位于程序目录 `logs`，记录启动、命令行参数、打开、压缩/解压/删除/校验、系统集成注册与图标恢复等关键节点及全部异常堆栈
- 反馈问题时请附带对应日期的日志文件

## 授权与致谢

- 压缩引擎为 [7-Zip](https://www.7-zip.org/)，遵循 LGPL 授权，许可文本见 `tools/License.txt`
- 安装包由 [Inno Setup](https://jrsoftware.org/isinfo.php) 生成；简体中文语言文件取自 [kira-96/Inno-Setup-Chinese-Simplified-Translation](https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation)（MIT）
- 主程序代码版权归项目作者所有
