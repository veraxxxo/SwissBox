# SwissBox

Мегаутилита-лаунчер открытых и легитимных Windows-программ в одном окне: обход блокировок, ИИ, драйверы, диагностика, разгон, офис и т.д. Один клик — программа скачивается/ставится и сразу открывается, без консоли.

## Сборка

Нужен только .NET Framework (входит в Windows), отдельный SDK не требуется.

```powershell
$csc = "C:\WINDOWS\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
& $csc /nologo /target:winexe /platform:anycpu /win32icon:SwissBox.ico /resource:SwissBox.ico,SwissBox.ico `
  /out:SwissBox.exe `
  /reference:System.dll,System.Windows.Forms.dll,System.Drawing.dll,System.IO.Compression.dll,System.IO.Compression.FileSystem.dll `
  SwissBox.cs
```

## Структура

- `SwissBox.cs` — всё приложение одним файлом (C# WinForms).
- `extension/` — расширение-браузера "SwissBox Download Catcher" (перехват загрузок/магнет-ссылок в aria2).
