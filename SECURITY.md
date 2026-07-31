# Security Policy

## Supported versions

| Version | Supported |
|---|---|
| 1.x | Yes |

## Reporting a vulnerability

请使用 GitHub 仓库 **Security → Report a vulnerability** 私密报告安全问题。

不要为未修复的安全问题创建公开 Issue，也不要附带含个人信息的截图、剪贴板内容或日志。

报告中请尽量包含：

- 受影响版本
- Windows 版本
- 复现步骤
- 影响范围
- 可行的缓解或修复建议

Snapshot 的截图、标注和 OCR 均在本机运行，不包含账户、自动更新或遥测。只有用户主动点击“翻译”时，OCR 文字会发送到用户在设置中配置的 Microsoft Translator HTTPS 地址；截图图片不会发送。翻译密钥使用 Windows DPAPI 加密保存。

除上述用户主动触发的翻译请求外，任何意外网络访问都应视为安全问题报告。
