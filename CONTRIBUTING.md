# Contributing to Snapshot

感谢你愿意帮助改进 Snapshot。

## 提交 Issue

- Bug 请说明 Windows 版本、显示器数量/DPI、复现步骤和预期结果。
- 快捷键问题请同时说明是否运行微信、QQ、PowerToys、显卡工具或其他截图软件。
- 不要在公开 Issue 上传含隐私信息的截图或日志。

## 本地开发

需要 Windows 10/11、Git 和 .NET 10 SDK。

```powershell
git clone https://github.com/wongzijie/Snapshot.git
cd Snapshot
dotnet build .\Snapshot.csproj -c Release
```

## Pull Request

1. 从 `main` 创建功能分支。
2. 保持改动范围聚焦。
3. 确保 `dotnet build -c Release` 零错误、零警告。
4. 涉及截图交互时，请测试整窗选择、自由选区、取消、剪贴板和 JPG 保存。
5. 在 PR 中说明改动原因、用户影响和验证方式。

请不要提交 `bin/`、`obj/`、`dist/` 或 ZIP 发布包。
