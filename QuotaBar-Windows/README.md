# QuotaBar for Windows

一个常驻桌面的小挂件，显示各家 AI 模型 / 平台的**剩余额度**，右键就能切换显示哪一个供应商。

这是 macOS 版 QuotaBar 的 **Windows 重写版**，不是移植 —— SwiftUI 没有 Windows 版本，界面层是全新写的。
额度接口的适配逻辑（字段路径、单位换算、错误处理）与 macOS 版一一对应。

## 先说三件 Windows 上必须知道的事

**1. Windows 没有 macOS 那种桌面小组件。**
Win11 的「小组件」面板只对微软生态开放，第三方程序没法把自己的组件放进去，更没法自由摆在桌面上。
所以这里的做法是：一个**无边框、透明背景、可拖动的小挂件窗口**，停在桌面上。效果和 macOS 版接近。

**2. 不需要装 .NET SDK、不需要 Visual Studio、不需要联网。**
编译用的是 Windows 自带的 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，
产物是一个独立的 `QuotaBar.exe`（依赖系统自带的 .NET Framework，Win10/11 都内置）。

**3. 编译出来的是未签名 exe，首次运行 Windows SmartScreen 会拦一下。**
点「更多信息」→「仍要运行」即可。这是所有没买代码签名证书的小工具的通用情况。

## 快速开始

```
1. 双击  build.cmd     ->  生成 QuotaBar.exe
2. 双击  QuotaBar.exe  ->  直接运行
   （或者双击 install.cmd，装到 %LOCALAPPDATA%\Programs\QuotaBar 并创建桌面快捷方式）
```

第一次运行时会自动弹出设置窗口。选一个供应商、填 API Key、点「保存并刷新」，桌面上就会出现额度挂件。

### 自检（推荐第一次跑一下）

```
QuotaBar.exe --selftest
```

会用内置的真实响应样本跑一遍全部解析逻辑（不联网），弹窗告诉你通过多少项，
完整报告写到 `%APPDATA%\QuotaBar\selftest.txt`。

## 支持的供应商

| 供应商 | 显示内容 | 数据来源 | 需要什么 |
| --- | --- | --- | --- |
| OpenRouter | 账户余额、本密钥剩余额度 | `/api/v1/credits`、`/api/v1/key` | API Key |
| DeepSeek | 账户余额（充值 / 赠送拆分） | `/user/balance` | API Key |
| Kimi (Moonshot) | 可用余额、代金券 | `/v1/users/me/balance` | API Key |
| SiliconFlow 硅基流动 | 可用余额、充值余额 | `/v1/user/info` | API Key |
| SoloAPI 中转站 | 账户余额、今日消费、累计消费、Token 用量 | `/v1/usage` | API Key |
| Claude Code 订阅 | 5 小时 / 7 天 / Opus / Sonnet 限额剩余百分比 + 重置倒计时 | `api.anthropic.com/api/oauth/usage` | 本机已登录 Claude Code |
| OpenAI 组织用量 | 本月花费、本月 Token | `/v1/organization/costs`、`/v1/organization/usage` | Admin Key（`sk-admin-`） |
| 自定义接口 | 任意返回 JSON 的接口 | 你自己填地址和字段路径 | 接口地址 |

关于「剩余额度」的实话：

* **OpenRouter / DeepSeek / Kimi / 硅基流动 / SoloAPI** 有官方余额接口，是真正意义上的「还剩多少钱」。
* **SoloAPI** 用的是它自家的接口，不是 one-api / new-api 那套约定：`GET /v1/usage` 一次返回余额、套餐名、今日消费、累计消费和 Token 用量（实测确认，余额单位 USD）。
* **Claude Code（Claude Pro / Max 订阅）** 没有公开额度 API，但 Anthropic 给 Claude Code 客户端用的 OAuth usage 接口会返回限额使用率，读的就是它。
* **OpenAI** 不提供任何「剩余额度」接口，ChatGPT 订阅额度也不对外。这里只能显示管理员密钥查得到的组织本月花费，界面上如实标注为「已用」。

> **从旧版本升级**：如果你之前把 SoloAPI 配成了「自定义接口」（指向对话用的 `/v1`、Key 塞在自定义请求头里），
> 打开新版会自动识别并升级成 SoloAPI 供应商，Key 会搬到凭证栏，不用重新填。

## 怎么操作

| 操作 | 效果 |
| --- | --- |
| 拖动挂件 | 移动位置（会自动记住） |
| 双击挂件 | 打开设置 |
| 右键挂件 | 菜单：刷新 / 切换供应商 / 只刷新当前 / 总在最前 / 贴到桌面 / 设置 / 退出 |
| 关掉挂件窗口 | 只是隐藏，进程继续在后台刷新 |
| 双击托盘图标 | 显示 / 隐藏挂件 |
| 右键托盘图标 | 显示隐藏挂件 / 立即刷新 / 设置 / 退出 |

「切换供应商」是带圆点的子菜单，点一下就换，挂件立刻切换显示。

「总在最前」（默认开）：挂件不会被其它窗口盖住。
「贴到桌面」（实验）：把挂件挂到桌面图层（图标后面），会被普通窗口盖住，像真正的桌面组件。
这个用的是 Rainmeter 那套 WorkerW 手法，不同系统版本表现可能不一致；如果挂件不见了，取消勾选再勾回来即可。

## 后台运行

程序常驻系统托盘（右下角通知区域），关掉窗口不会退出：

* 关掉挂件窗口只是把它藏起来，进程还在，会继续按设定间隔刷新数据，托盘图标的悬停提示也会跟着更新成当前额度。
* 右键托盘图标：显示 / 隐藏挂件、立即刷新、设置、退出。
* **只有点托盘菜单里的「退出」才会真正结束进程。**
* 想开机就有挂件：设置里勾上「开机自动启动」（写注册表 Run 项，不需要管理员权限）。

## 设置项

| 项目 | 说明 |
| --- | --- |
| 自动刷新 | 1 / 2 / 5 / 10 / 30 / 60 分钟，默认 10 分钟 |
| 总在最前 | 见上 |
| 贴到桌面 | 见上 |
| 开机自动启动 | 写 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，不需要管理员权限 |

**关于 1 分钟刷新**：适合盯 Claude Code 的 5 小时窗口。但余额类接口（OpenRouter / DeepSeek / Kimi）
一般有频率限制，长期 1 分钟一次等于每天 1440 次请求，可能被限流甚至被风控。日常建议 5–10 分钟，
要看紧的时候再临时调到 1 分钟。

## 配置文件

```
%APPDATA%\QuotaBar\config.json
```

设置窗口里点「打开文件夹」可以直接跳过去。

## 关于安全

API Key 和 Claude Code 的 OAuth 令牌以**明文**存在上面那个 `config.json` 里。
这是为了让挂件能独立请求（否则关掉设置窗口就没数据了）。如果机器多人共用，请谨慎填写长期密钥。

Claude Code 那一项会读取 `%USERPROFILE%\.claude\.credentials.json`。
access token 过期时会用 refresh token 续期，并**写回原文件** —— 这是必须的，
因为 Anthropic 的 refresh token 是轮换的，不回写会把 Claude Code 自己的登录态搞坏。

## 已知限制

* 挂件是未签名程序，首次运行需要放行 SmartScreen。
* 余额类接口一般有频率限制，默认 10 分钟刷新一次，不建议调到 5 分钟以下。
* 各家返回结构可能随时变；解析失败时挂件上会直接显示原始错误信息，方便定位。
* 「贴到桌面」是实验特性，见上文。
* 多显示器和不同 DPI 缩放下，挂件位置记忆可能有几像素偏差。

## 排错

**双击没反应 / 闪一下就没了**
用命令行运行 `QuotaBar.exe`（或先跑 `--selftest`）看报错。也可能是已经在运行了（会弹提示）。

**挂件显示「还没有添加供应商」**
双击挂件打开设置，添加一个供应商。

**挂了几天数据不动了**
右键 →「刷新」。如果一直报错，多半是 API Key 失效或欠费。

**build.cmd 报「找不到 csc.exe」**
控制面板 →「启用或关闭 Windows 功能」→ 勾上 .NET Framework 4.x。

## 代码结构

```
QuotaBar-Windows/
├── build.cmd            用系统自带 csc.exe 编译
├── install.cmd / uninstall.cmd   安装 / 卸载（文件名用英文，避免中文名在 zip 里乱码）
└── src/
    ├── Json.cs            极简 JSON 解析 / 生成（零第三方依赖）
    ├── Models.cs          额度模型、供应商配置、配置读写
    ├── Providers.cs       六家供应商 + 自定义接口，以及 HTTP 封装
    ├── WidgetWindow.cs    桌面挂件（无边框窗口、右键菜单、桌面图层）
    ├── SettingsWindow.cs  设置界面（动态生成控件，没有 XAML）
    ├── TrayIcon.cs        托盘图标（后台常驻，图标是运行时画出来的，不需要 .ico 文件）
    ├── Program.cs         入口、单实例、控制器
    └── SelfTest.cs        --selftest 自检
```

没有用 XAML，界面全部在 C# 里构建 —— 这样编译只需要一条 `csc.exe` 命令，
不用 MSBuild、不用项目文件、不用 SDK。

### 想加一家供应商

在 `Providers.cs` 里加一个 `FetchXxx` + `ParseXxx`，然后在 `ProviderEngine.Fetch`
和 `ProviderCatalog` 的几个 `switch` 里加上分支即可。
如果只是接一个私有接口，不用改代码，用「自定义接口」类型填地址和字段路径就行。

## 当前状态

这份代码是在 macOS 上写的，**没有在 Windows 上编译验证过**（开发机上没有 Windows、没有 .NET，
也没有网络装工具链）。已经做过的检查：

* C# 5 语法兼容性扫描（Windows 自带的 csc 只支持到 C# 5，不能用字符串插值、`?.` 等）
* 全部文件的括号 / 引号配平校验
* 跨文件成员名交叉引用校验
* 指标构造函数参数个数校验

如果 `build.cmd` 报错，把错误信息发我，我直接改。

## 环境要求

Windows 10 / 11，需要启用 .NET Framework 4.x（系统默认就有）。
