@echo off
cd /d "%~dp0"
dotnet "Tools\ConfigExporter\src\ConfigExporter\bin\Debug\net8.0\ConfigExporter.dll" --input "Config\Excel\游戏数值配置.xlsx" --schema "Config\Schema\配置表结构.json" --output "Config\Generated" --strict
if errorlevel 1 goto fail
echo [OK] 导出完成。请回 Unity 点菜单 Tools - 游戏配置 - 导入并校验。
pause
exit /b 0
:fail
echo [FAIL] 导出失败。常见原因：Excel 未关闭导致文件被占用。
pause
exit /b 1
