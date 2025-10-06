# UHR Info Detector

## 功能清单
- 启动自检：启动时验证内置 `svn.exe` 是否可用，并从 `UHR_CUSTOMIZED` 路径抓取所有以 `_HRDB` 结尾的机构目录填充列表，同时支持关键字过滤与快捷 Enter/双击触发分析。
- 目标版本管理：读取 `nho-hospnet3/trunk/doc/06.UHR/99.发布成果` 下 Core/Web給与/年调模块的版本目录，自动剥离前缀、排序并填充目标版本下拉框，高亮当前配置与目标一致的组合。
- 组织版本解析：针对选定机构，远程 `svn cat` 其 `CONF_SYSCONTROL.SQL`，解析 `FrameVersion`、`UhrCore_Version`、`UhrSalary_Version`、`UhrNencho_Version` 等配置并显示于界面。
- 定制文件清单：连接 Oracle 中的 `module_info_{机构码}` 表读取文件名、Hash、功能版本及定制标志，缓存结果、列出 `CUSTOMIZEDFLG=1` 的定制文件并标注 Smart Company 的专用 jar。
- 版本差异评估：比较当前版本与目标版本之间所有中间版本，扫描每个模块版本的文件列表，交叉匹配定制文件名以判断合并风险，并将需要人工合并的文件整理到列表。
- 定制文件打包：`准备定制文件` 功能依据 ModuleInfo 中的 MD5 哈希在 SVN 中定位并下载对应文件，保持原有目录结构输出到本地 `_CustomizedFiles` 目录，同时生成成功/缺失日志。
- 合并文件打包：`准备合并文件` 会为每个合并候选项定位目标版本文件，优先 `svn export`、必要时回退 `svn cat`，并在桌面创建时间戳目录归档，复用缓存提升多文件下载效率。
- 报告与输出：`生成报告` 打开 RTF 预览窗口，汇总机构名称、各模块版本、定制数量与合并清单，支持一键复制为富文本/HTML 以便粘贴到邮件或工单。
- 运行时保障：统一的状态栏提示、按钮显隐控制及临时目录清理，并在 `DebugLog.txt`/`ErrorLog.txt` 中记录 SVN 调用、超时、回退等细节，保障操作可追溯。
