@echo off
chcp 65001 >nul
rem ============================================================
rem  上传资源包到腾讯云 COS（每次打完包双击运行一次即可）
rem
rem  首次使用前：
rem   1) 安装 coscmd：  pip install coscmd
rem   2) 把下面 config 一行里的 "你的SecretId" "你的SecretKey"
rem      换成真实密钥（腾讯云控制台 -> 访问管理 -> 访问密钥 -> API密钥管理）
rem   3) 运行一次后，可在 config 行前面加 rem 注释掉（配置只需执行一次）
rem ============================================================

rem ---- 首次配置（仅需执行一次，配完可注释掉这行）----
coscmd config -a 你的SecretId -s 你的SecretKey -b 7-2026824-1412305634 -r ap-guangzhou

rem ---- 递归上传整个 webgl 目录到桶根目录（同名覆盖，旧文件保留）----
rem -H 给每个文件写入 Cache-Control 长缓存头：文件名带内容哈希，
rem     内容变化 => 哈希变化 => 新文件名，因此可以安全缓存一年。
rem     已上传的旧文件也会被本次覆盖并补上缓存头（微信「未命中 CDN 缓存」即由此而来）。
coscmd upload -r -f -H {"Cache-Control":"public,max-age=31536000"} "E:\BaiduNetdiskDownload\毕设\My project - new\name7\webgl" /

echo.
echo ==================== 上传完成 ====================
echo 验证缓存头（应看到 Cache-Control: public, max-age=31536000）：
echo curl -I https://7-2026824-1412305634.cos.ap-guangzhou.myqcloud.com/994d88a6233c8c42.webgl.data.unityweb.bin.txt
echo.
echo 说明：哈希以 game.js 里的 DATA_FILE_MD5 为准（每次打新包都会变）。
echo 可选：在 COS 控制台 -> 桶 -> 文件默认配置 里把 Cache-Control
echo       也设为 public, max-age=31536000，则手动/控制台上传的文件同样生效。
echo.
pause
