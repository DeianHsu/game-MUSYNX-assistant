# MUSYNX 自动打歌工具

同步音律喵赛克（Steam 原版）的自动演奏工具。不截图、不识别下落音符，直接按游戏内部的谱面时间和判定接口来打，全程和手打走同一套判定。

## 功能

- 选歌、选键数（4K / 6K）、选难度后一键开打
- 歌曲列表支持按歌名 / 难度 / 键数 / 等级 / 同步率排序，支持歌名搜索与筛选
- 打完回到选歌会自动刷新同步率
- 游戏内 **F11** 切换全屏 / 窗口
- 切出窗口不会自动暂停

## 安装

1. 下载 Release 里的 **`MUSYNX-Tool.exe`**（单文件，约 1.4 MB，不用解压、不用安装）
2. 双击打开
3. 「游戏目录」选到 `...\steamapps\common\MUSYNX`（里面要有 `MUSYNX.exe`）
4. 点「安装/修复」

安装会把加载器和插件写进游戏目录，并关掉启动时那个分辨率选择窗口。原始文件会备份在 `BepInEx\MusynxAutoplayBackup\`。

要还原就点旁边的「卸载」，它会恢复备份并删掉写进去的文件。

## 使用

1. 自己从 Steam 启动游戏
2. 打开工具，点「进入选歌」，等游戏切到选歌界面
3. 点「刷新列表」
4. 选一首，点「开始打歌」

打歌过程中按 **F9** 可以随时开关自动演奏。

## 配置

首次运行后生成：`游戏目录\BepInEx\config\local.musynx.autoplay.cfg`

| 配置项 | 默认 | 说明 |
| --- | --- | --- |
| `Enabled` | true | 进歌后自动开始 |
| `Mode` | Native | Native 走游戏输入接口；Perfect 直接按谱面时间判定（实验） |
| `OffsetMs` | 0 | 整体提前量，正值更早 |
| `ToggleKey` | F9 | 开关热键 |
| `FullscreenKey` | F11 | 全屏切换热键 |
| `BlockUpload` | true | 是否拦截排行榜上传 |
| `IgnoreFocusPause` | true | 切出窗口是否暂停 |

## 关于排行榜

游戏在结算时会把本局同步率上传到 Steam 的全球排行榜。自动打出来的分数传上去就是作弊，所以工具**默认拦截上传**。

拦截之后：

- 只有本地成绩会被记录，工具里看到的同步率、EX、连击都是本地数据
- 你自己正常手打的成绩也不会传（因为拦截的是整个上传入口）

如果你确实想恢复上传，把配置里的 `BlockUpload` 改成 `false`。**不建议在自动打歌时打开。**

## 卸载

点工具里的「卸载」按钮，会恢复 `globalgamemanagers` 备份并删除加载器和插件。

手动卸载也可以：删除游戏目录下的 `BepInEx`、`winhttp.dll`、`doorstop_config.ini`、`.doorstop_version`，再把 `BepInEx\MusynxAutoplayBackup\globalgamemanagers.orig` 覆盖回 `MUSYNX_Data\globalgamemanagers`。

## 常见问题

**双击 exe 弹「无法验证发布者」**
右键 exe → 属性 → 勾「解除锁定」→ 确定；或者直接点「仍要运行」。工具没有数字签名，Windows 对刚下载的 exe 会提示一次。

**全屏黑屏**
不要用启动参数强制全屏。正常窗口模式进游戏，进去后按 **F11** 切换。

**Steam 验证完整性之后失效**
验证会还原 `globalgamemanagers` 并可能覆盖插件。重新点一次「安装/修复」即可。

**杀软拦截**
工具没有数字签名，360 之类可能会拦。加信任即可。
