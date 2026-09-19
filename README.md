# Стиснення без втрат: Гаффман і Шеннон–Фано

Цей репозиторій містить два незалежні навчальні проєкти C# для стиснення й відновлення файлів та рядків:

| Проєкт | Алгоритм | Формат архіву | Інструкція |
| --- | --- | --- | --- |
| `HuffmanCoding` | Код Гаффмана | `HUF1`, зазвичай `.huf` | [README](HuffmanCoding/README.md) |
| `ShannonFanoCoding` | Код Шеннона–Фано | `SFC1`, зазвичай `.sfc` | [README](ShannonFanoCoding/README.md) |

У кожній папці є окрема програма `Compress` і окрема програма `Decompress`. Частоти байтів обчислюються для кожного входу та зберігаються в архіві. Формати різні: декодер одного проєкту не читає архів іншого.

Потрібен .NET SDK 8. Для збирання та перевірки з кореня репозиторію:

```powershell
dotnet build HuffmanCoding/Compress/HuffmanCompress.csproj -c Release
dotnet build HuffmanCoding/Decompress/HuffmanDecompress.csproj -c Release
dotnet build ShannonFanoCoding/Compress/ShannonFanoCompress.csproj -c Release
dotnet build ShannonFanoCoding/Decompress/ShannonFanoDecompress.csproj -c Release
./tests/verify.ps1
```

Скрипт перевірки створює тимчасові файли в системній тимчасовій папці й видаляє їх після завершення. Приклади команд і докладне пояснення коду наведено у README кожного проєкту.
