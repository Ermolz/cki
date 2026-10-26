# Кодування та стиснення даних

Цей репозиторій містить п'ять незалежних навчальних проєктів C# для стиснення даних або захисту від бітових помилок. Усі проєкти працюють із файлами та рядками:

| Проєкт | Алгоритм | Формат файла | Інструкція |
| --- | --- | --- | --- |
| `HuffmanCoding` | Код Гаффмана | `HUF1`, зазвичай `.huf` | [README](HuffmanCoding/README.md) |
| `ShannonFanoCoding` | Код Шеннона–Фано | `SFC1`, зазвичай `.sfc` | [README](ShannonFanoCoding/README.md) |
| `ArithmeticCoding` | Арифметичний код | `ARC1`, зазвичай `.arc` | [README](ArithmeticCoding/README.md) |
| `Lz78Coding` | Код LZ-78 | `LZ78`, зазвичай `.lz78` | [README](Lz78Coding/README.md) |
| `HammingCoding` | Розширений код Геммінга `(8,4)` | Пари кодових блоків, зазвичай `.ham` | [README](HammingCoding/README.md) |

У проєктах стиснення є окремі програми `Compress` і `Decompress`. Гаффман, Шеннон–Фано й арифметичний код обчислюють частоти байтів для кожного входу та зберігають їх в архіві. LZ-78 будує словник під час роботи; декодер відтворює його з кодового потоку. У `HammingCoding` програма `Encode` додає перевірочні біти, а `Decode` виправляє одну помилку в кожному восьмибітному блоці й відновлює дані. Кодування Геммінга збільшує обсяг даних удвічі. Кожен декодер призначений для формату свого проєкту.

Потрібен .NET SDK 8. Для збирання та перевірки з кореня репозиторію:

```powershell
dotnet build HuffmanCoding/Compress/HuffmanCompress.csproj -c Release
dotnet build HuffmanCoding/Decompress/HuffmanDecompress.csproj -c Release
dotnet build ShannonFanoCoding/Compress/ShannonFanoCompress.csproj -c Release
dotnet build ShannonFanoCoding/Decompress/ShannonFanoDecompress.csproj -c Release
dotnet build ArithmeticCoding/Compress/ArithmeticCompress.csproj -c Release
dotnet build ArithmeticCoding/Decompress/ArithmeticDecompress.csproj -c Release
dotnet build Lz78Coding/Compress/Lz78Compress.csproj -c Release
dotnet build Lz78Coding/Decompress/Lz78Decompress.csproj -c Release
dotnet build HammingCoding/Encode/HammingEncode.csproj -c Release
dotnet build HammingCoding/Decode/HammingDecode.csproj -c Release
./tests/verify.ps1
./ArithmeticCoding/tests/verify.ps1
./Lz78Coding/tests/verify.ps1
./HammingCoding/tests/verify.ps1
```

Скрипти перевірки створюють тимчасові файли в системній тимчасовій папці й видаляють їх після завершення. Приклади команд і докладне пояснення коду наведено у README кожного проєкту.
