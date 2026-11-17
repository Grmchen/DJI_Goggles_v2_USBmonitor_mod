## 前置需求
1. 你的DJI_goggles_v2是被root后安装了moonlight的
2. 你的系统装了NET10.0.0及以上
3. 该文件的路径不能有中文，没有为什么
## Root方法
1. 确保你的眼睛版本是正确的版本然后访问https://fpv.wtf/ 来进行Root操作
如果你的眼镜的版本不支持的话那么你可以选择去https://github.com/fpv-wtf/butter 来进行降级
2. Root完之后刷上风扇控制和moonlight

### 免责声明：本人只提供方法和链接如在执行操作过程中发生的任何风险应由操作人承担。

## 用法

1. 眼镜用 **USB 线**连电脑，在FPV模式进入菜单选择 **Moonlight** ，屏幕变成一张全屏图，这时就可以启动投屏软件了。
2. 双击 `GogglesUsb.exe`
3. 点 **开始投屏**

关窗口 = 停止并结束所有进程。

## 使用组件

本仓库代码：**MIT**。

运行时组件（本仓库不包含，便携包内自带）：

- [Sunshine](https://github.com/LizardByte/Sunshine) —— GPL-3.0
- [dji-moonlight-gui](https://github.com/fpv-wtf/dji-moonlight-gui) / [dji-moonlight-embedded](https://github.com/fpv-wtf/dji-moonlight-embedded) —— GPL-3.0
- [dji-moonlight-shim](https://github.com/fpv-wtf/dji-moonlight-shim) —— 眼镜端，需自行安装

如果重新分发带这些二进制的便携包，请遵守对应 GPL 许可。
