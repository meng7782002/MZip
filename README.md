# QuickZip

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
- 最近打开记录（最多 10 条）、拖拽打开、中文路径无乱码

### Windows 系统集成
- 设置中可注册为 `.zip` / `.rar` / `.7z` 默认解压程序（仅当前用户 HKCU，无需管理员权限）
- 一键跳转系统“默认应用”设置页
- 资源管理器右键菜单：
  - 文件 / 文件夹 / 多选 → “使用 QuickZip 压缩”，多选自动汇总为一个压缩包
  - `.zip` / `.rar` / `.7z` → “解压到本目录”（解压到同名子文件夹）、“解压到指定目录…”
- 注册后压缩文件统一显示 QuickZip 图标；取消关联时按备份恢复原图标

## 目录结构

```
QuickZip.slnx
├─ src/QuickZip/                 主程序
│  ├─ App.xaml(.cs)              启动与命令行解析
│  ├─ MainWindow.xaml(.cs)       主界面
│  ├─ Dialogs/                   创建、密码、覆盖策略、设置、关于
│  ├─ Models/                    实体类
│  ├─ Services/                  7-Zip 引擎、进程执行、设置、日志、系统集成
│  ├─ Assets/QuickZip.ico        应用图标
│  └─ tools/                     7z.exe / 7z.dll / License.txt
├─ tools/                        7-Zip 原始文件
└─ docs/                         需求、设计、使用说明（按时间前缀分版本）
```

程序运行时在输出目录下生成：

- `config/settings.json`：明文 JSON 设置
- `logs/quickzip<yyyy-MM-dd>.log`：按天滚动的日志

## 构建与运行

环境要求：Windows 10/11 x64、.NET 8 SDK（运行需 .NET 8 Desktop Runtime）。

```powershell
git clone <仓库地址>
cd 解压缩
dotnet build QuickZip.slnx -c Release
```

运行：

```powershell
src\QuickZip\bin\Release\net8.0-windows\QuickZip.exe
```

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| `QuickZip.exe "压缩包路径"` | 打开指定压缩包 |
| `QuickZip.exe --compress "路径1" ["路径2" …]` | 一键压缩为 7Z |
| `QuickZip.exe --extract-here "压缩包路径"` | 解压到压缩包同级同名文件夹 |
| `QuickZip.exe --extract-to "压缩包路径"` | 选择目录后解压 |

## 文档

- [需求规格说明书 v1.3](docs/202611021353_需求规格说明书_v1.3.md)
- [技术方案设计 v1.4](docs/202611021353_技术方案设计_v1.4.md)
- [使用说明 v1.3](docs/202611021353_使用说明_v1.3.md)
- 历史版本按“年月日时分 + 文档名 + 版本号”保存在 `docs/` 中

## 日志与问题反馈

- 日志位于程序目录 `logs`，记录启动、命令行参数、打开、压缩/解压/删除/校验、系统集成注册与图标恢复等关键节点及全部异常堆栈
- 反馈问题时请附带对应日期的日志文件

## 授权与致谢

- 压缩引擎为 [7-Zip](https://www.7-zip.org/)，遵循 LGPL 授权，许可文本见 `tools/License.txt`
- 主程序代码版权归项目作者所有
