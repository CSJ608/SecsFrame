# SecsFrame 本机 Demo 包

此包包含通讯测试工具、分步成果演示和统一启动器。需要已安装 .NET 8
ASP.NET Core Runtime；默认只监听本机回环地址。

Windows：

~~~text
start-demos.cmd
~~~

Linux/macOS：

~~~text
sh start-demos.sh
~~~

启动器等待两个应用的专用健康检查通过后打开浏览器。默认地址为
<code>http://127.0.0.1:5080</code> 和
<code>http://127.0.0.1:5081</code>。使用 <code>--help</code> 查看改端口、
不打开浏览器和启动验证选项；按 <code>Ctrl+C</code> 同时停止两个应用。

工程回环、固定脚本和成功启动都不代表设备 Profile、SEMI 一致性或生产
就绪。通讯工具的活动导出不提供 Raw 分级；分享导出文件前仍须遵守项目
自身的访问控制和保留策略。
