# ExamAnswerSheet

WPF (.NET 8) answer sheet for practice exams. No NuGet packages.

## Open / run
**Visual Studio 2022 (17.8 or newer, workload ".NET desktop development")**
1. File > Open > Project/Solution > pick `ExamAnswerSheet.csproj`
2. Press F5.

**dotnet CLI** (needs the .NET 8 SDK, Windows)
```
cd ExamAnswerSheet
dotnet run
```

## Publish ONE self-contained exe
```
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```
Output: `bin\Release\net8.0-windows\win-x64\publish\`
- `ExamAnswerSheet.exe`  (the app)
- `config.json`, `key.json` (editable, keep them next to the exe)

## Files it writes
- Auto-save: `%AppData%\ExamAnswerSheet\state.json`
- Finished exams: `Documents\ExamAnswerSheet\Exam_YYYY-MM-DD_HH-mm.txt`

## config.json
- `totalQuestions` counts the PBQs too: PBQs are Q1-Q5, the grid runs from 6 to `totalQuestions`.
- PBQ `format`: `answer-id` -> `C-1a`, `id-answer` -> `2a-B`, `inline` -> `3.CEFADB`
- item `type`: `letter` (+ `letters`), `order` (+ `letters` for the click buttons), `allowdeny`
