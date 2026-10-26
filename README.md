![Snapshot：Windows截图、标注、本机OCR与可选翻译](docs/assets/showcase/cover.png)



<p align="center">
  <a href="https://github.com/wongzijie/Snapshot/releases/latest"><img alt="GitHub Release" src="https://img.shields.io/github/v/release/wongzijie/Snapshot?style=flat-square&color=07C160"></a>
  <a href="https://github.com/wongzijie/Snapshot/actions/workflows/build-and-release.yml"><img alt="Build" src="https://img.shields.io/github/actions/workflow/status/wongzijie/Snapshot/build-and-release.yml?style=flat-square&label=build"></a>
  <img alt="Windows" src="https://img.shields.io/badge/Windows-10%20%7C%2011-1677FF?style=flat-square">
  <img alt=".NET" src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square">
</p>

<p align="center">
  <strong>微信式选区体验 + macOS 式快捷直存。</strong><br>
  一款轻量、常驻托盘，支持标注、OCR 与可选翻译的 Windows 截图工具。
</p>

<p align="center">
  简体中文 · <a href="README_EN.md">English</a>
</p>

## 为什么是 Snapshot？

大多数截图工具要么只复制到剪贴板，要么每次都弹出保存对话框。Snapshot 用两套独立的全局快捷键解决这两个高频场景：

- **复制模式**：选完即进入剪贴板，直接粘贴到聊天、文档或设计工具。
- **保存模式**：选完直接写入本地 JPG，不弹保存对话框。
- **智能窗口识别**：移动鼠标即可吸附当前窗口，也能自由拖出任意区域。
- **选区可调整**：选定后可以移动选区、拖动边缘或控制点缩放。
- **截图编辑**：画笔、箭头、马赛克，以及无底色、可再次编辑和自由拖放的独立文本框；支持字体、字号、字形和颜色。
- **本机 OCR**：使用 Windows 已安装的 OCR 语言包，不上传截图。
- **图片原位翻译**：OCR 后逐行翻译，遮盖原文并把译文自动回绘到图片中的对应位置；不配置密钥就不会发起翻译请求。

<p align="center">
  <img src="docs/assets/workflow.svg" alt="Snapshot capture workflow" width="92%">
</p>

## 图解：截图与翻译的数据边界

![图解：截图与翻译的数据边界，流程示意而非运行截图](docs/assets/showcase/workflow.png)

功能流程示意，非Windows实机截图。本机OCR与主动发起的云端文字翻译是两条不同的数据路径；详细行为和限制见下文。

[查看全部图片与来源](docs/assets/showcase/README.md)

## 下载

前往 [GitHub Releases](https://github.com/wongzijie/Snapshot/releases/latest)，下载：

```text
Snapshot-v1.2.2-win-x64.zip
```

解压后双击 `Snapshot.exe`。发布包已包含 .NET 运行时，不需要单独安装 .NET。

> [!NOTE]
> 当前版本尚未进行商业代码签名，Windows 可能显示“未知发布者”。如有顾虑，请核对 Release 页面提供的 SHA-256，或直接从源码构建。

## 快速开始

| 操作 | 默认快捷键 | 结果 |
|---|---|---|
| 截图到剪贴板 | `Ctrl + Alt + A` | 可立即在其他应用中粘贴 |
| 截图并保存 | `Ctrl + Alt + S` | JPG 直接保存到系统“下载”文件夹 |

触发快捷键后：

1. 移动鼠标，Snapshot 会自动框选鼠标下方的窗口。
2. 单击可选择整个窗口；按住左键拖动可创建自定义区域。
3. 选定后可拖动选区移动，或拖动边缘/控制点缩放。
4. 可使用工具栏添加画笔、箭头、文字或马赛克；文字框无底色，可再次点选编辑。拖动正文可框选文字，拖动绿色虚线边框可移动文本框；`Ctrl + Z` 撤销，`Ctrl + Y` 重做。
5. 点击 `OCR` 在本机提取文字；点击“翻译”会生成覆盖原文字位置的译图预览。
6. 按 `Enter`、双击选区或点击绿色按钮完成。
7. 按 `Esc` 或单击鼠标右键取消。

更多说明请查看 [完整使用指南](docs/USAGE.zh-CN.md)。

## 设置

程序启动后常驻系统托盘：

- 双击绿色 Snapshot 图标打开设置。
- 右键图标可立即截图、打开保存目录或退出。
- 可分别修改两套全局快捷键。
- 可修改 JPG 保存目录和图片质量（60%–100%）。
- 可选择 OCR 语言和翻译目标语言。
- 可选配置 Microsoft Translator 服务地址、密钥与区域。

两套快捷键必须不同，并包含 `Ctrl`、`Alt`、`Shift` 或 `Win` 中的至少一个修饰键。

## 文件位置

| 内容 | 默认位置 |
|---|---|
| JPG 截图 | Windows 系统“下载”文件夹 |
| 设置 | `%AppData%\Snapshot\settings.json` |
| 本地日志 | `%LocalAppData%\Snapshot\Snapshot.log` |

日志只记录启动、截图尺寸和错误信息，不包含截图像素、OCR 正文、翻译正文、密钥或剪贴板内容。

## 从源码构建

环境要求：

- Windows 10 版本 2004 或更高版本 / Windows 11 x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```powershell
git clone https://github.com/wongzijie/Snapshot.git
cd Snapshot
dotnet build .\Snapshot.csproj -c Release
```

生成自包含单文件版本：

```powershell
.\build-release.ps1
```

输出位于 `dist\Snapshot.exe`。

## 当前范围

Snapshot v1.2 功能：

- [x] 全局快捷键
- [x] 窗口识别
- [x] 自由选区
- [x] 选区移动与缩放
- [x] 剪贴板复制
- [x] JPG 快捷保存
- [x] 多显示器虚拟桌面
- [x] 画笔、箭头与画布内联文字标注
- [x] 文字字体、字号、字形与颜色设置
- [x] 独立文字对象的二次编辑和自由拖放
- [x] 马赛克
- [x] Windows 本机 OCR
- [x] 可选 Microsoft Translator 图片原位翻译
- [ ] 截图贴图

> [!IMPORTANT]
> 截图、标注、马赛克和 OCR 始终在本机完成。只有主动点击“翻译”时，OCR 识别出的文字才会发送到用户配置的 Microsoft Translator；截图图片本身不会发送。Translator 密钥通过 Windows DPAPI 绑定当前用户加密保存。

## 参与项目

Bug 和功能建议欢迎通过 [Issues](https://github.com/wongzijie/Snapshot/issues) 提交。贡献代码前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。

安全问题请不要创建公开 Issue，详见 [SECURITY.md](SECURITY.md)。

## 说明

Snapshot 的交互灵感来自微信截图与 macOS 系统截图，但与腾讯或 Apple 无隶属、授权或合作关系。

## 更多项目

[A800 动量回测](https://github.com/wongzijie/stock_database) · [OpenClaw 编排经验](https://github.com/wongzijie/OpenClaw-Experience) · [Spark Memory 设计](https://github.com/wongzijie/spark-memory) · [Buffett Archive 本地检索](https://github.com/wongzijie/buffett-archive) · [相册快送](https://github.com/wongzijie/album-send)
