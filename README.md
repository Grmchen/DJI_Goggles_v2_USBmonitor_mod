## 前置需求
需要将你的DJI_V2眼镜Root并刷入其他辅助模块以及最主要的moonlight。


## 用法

1. 眼镜用 **USB 线**连电脑，菜单进 **Moonlight**（shim），屏幕变成一张全屏图 = 接管成功（按 **BACK** 退出）
2. 双击 `GogglesUsb.exe`
3. 点 **开始投屏**

关窗口 = 停止并结束所有进程。

> 转发器里眼镜地址写死为 `192.168.42.5`（`dji_net.c` 里 `inet_addr("192.168.42.5")`）。
> USB 连上后眼镜就是这块网卡的 DHCP 服务器，一般就是 `.5`。

## 其他组件

本仓库代码：**MIT**。

运行时组件（本仓库不包含，便携包内自带）：

- [Sunshine](https://github.com/LizardByte/Sunshine) —— GPL-3.0
- [dji-moonlight-gui](https://github.com/fpv-wtf/dji-moonlight-gui) / [dji-moonlight-embedded](https://github.com/fpv-wtf/dji-moonlight-embedded) —— GPL-3.0
- [dji-moonlight-shim](https://github.com/fpv-wtf/dji-moonlight-shim) —— 眼镜端，需自行安装

如果重新分发带这些二进制的便携包，请遵守对应 GPL 许可。
