# PoE Trade Search

Path of Exile 1용 거래소 검색 도구 (C# / WPF, .NET 11).

영문(글로벌) 클라이언트와 한글(카카오) 클라이언트를 모두 지원합니다. 복사한 아이템이 한글이면 카카오 거래소(poe.kakaogames.com), 영문이면 글로벌 거래소(pathofexile.com)로 자동으로 검색합니다. 두 거래소의 매물은 같고 표시 언어만 다릅니다.

게임 중 아이템에 마우스를 올리고 단축키(기본 `Ctrl+D`)를 누르면 상세 복사(Ctrl+Alt+C)로 아이템 텍스트를 복사하고,
옵션을 파싱해서 거래소에서 비슷한 아이템을 검색합니다. 프로그램이 켜져 있는 동안 화면 맨 위 가운데에 작은 "실행 중" 표시가 뜹니다(설정에서 끌 수 있음).

## 구조

| 프로젝트 | 역할 |
|---|---|
| `src/PoETradeSearch.Core` | UI와 무관한 로직: 아이템 텍스트 파서, stat 매칭, 거래소 쿼리와 API 클라이언트, 게임 데이터(`Data/`) |
| `src/PoETradeSearch.App` | WPF 앱(MVVM): `ViewModels/`에 화면 상태와 명령, 창(`*.xaml`)은 바인딩만. 전역 단축키, 상세 복사 전송, 실행 중 표시 |
| `tests/PoETradeSearch.Tests` | xUnit 테스트 (파서, 매칭, 쿼리, 검색 창 뷰모델, 게임 데이터) |

```
src/PoETradeSearch.Core/
  Data/         game-data.json(시즌 데이터)과 읽는 코드
  Parsing/      아이템 텍스트 파서
  Trade/        stat 매칭, 쿼리 생성, 거래소 API
src/PoETradeSearch.App/
  Views/        창(설정, 검색, 실행 중 표시)과 개발용 옵션
  ViewModels/   창의 상태와 명령
  Converters/   바인딩 변환기
  Services/     설정, 단축키, 클립보드, 거래소 데이터 캐시
  Native/       Windows API
  Resources/    테마(색, 버튼, 칩 스타일)
  Assets/       아이콘
```

솔루션(`PoETradeSearch.slnx`)에는 프로젝트 외에 `data`(game-data.json), `build`(릴리스 워크플로, 아이콘 스크립트), `docs` 폴더가 있습니다.

앱은 MVVM 구조입니다(CommunityToolkit.Mvvm).

- `SearchViewModel`: 검색 창의 조건, 옵션 줄, 결과, 검색 명령. `BuildOptions()`가 지금 화면 상태를 거래소 쿼리로 만들어서 창 없이 테스트할 수 있습니다.
- `MainViewModel`: 설정(서버, 리그, 화면 크기, 체크 옵션)과 단축키 → 복사 → 검색 흐름, 게임 종료 감시.
- 창 코드에는 창 자체에 관한 것(위치, 크기 배율, 드래그, 단축키 입력 받기)만 남겼습니다.

## 시즌이 바뀌었을 때: 게임 데이터 파일

시즌마다 바뀔 수 있는 게임 용어와 거래소 필터는 코드가 아니라 `src/PoETradeSearch.Core/Data/game-data.json`에 있습니다.

| 항목 | 내용 |
|---|---|
| `itemFlags` | 아이템 아래 붙는 표시(분열된 아이템, 결합된 아이템, 작열의 총주교 아이템 등). `tradeFilter`가 있으면 검색 창에 칩으로 나오고 거래소 '기타' 필터로 검색합니다 |
| `influences` | 영향력과 칩 이름 |
| `namePrefixes` | 이름 앞에 붙는 말(삿된, 결합된) |
| `modHeaders` | 상세 복사 `{ … }` 줄에서 고정·타락·변이 옵션을 알아보는 말 |
| `variantStats`, `clusterItemLevelSteps` | 정확한 값으로 검색하는 변종 옵션, 스킬 군 주얼 레벨 구간 |
| `currencyNames`, `itemCategories` | 결과의 화폐 이름, 아이템 종류 → 거래소 분류 |
| `itemClassRules` | 아이템 종류별 규칙. 예: 살덩이는 "상위/하위"를 떼고 검색, 희귀도 줄이 없으면 일반, 고정 옵션 기본 체크 |
| `usageHints` | "마을이나 은신처에…" 같은 사용 안내 문장의 시작. 옵션으로 읽지 않습니다 |

새 시즌 메커니즘이 생기면 보통 `itemFlags`에 한 줄을 추가하면 끝입니다(게임이 복사하는 글자, 거래소 필터 id, 칩 이름). 거래소 필터 id는 `https://poe.kakaogames.com/api/trade/data/filters`에서 찾을 수 있습니다.

이 파일은 exe 안에 들어가므로(내장 리소스) 따로 배포할 필요가 없습니다. 보통은 파일을 고친 뒤 새 버전을 릴리스합니다.

급하게 다시 빌드하지 않고 바꾸려면 이 파일을 `%LOCALAPPDATA%\PoETradeSearch\game-data.json`으로 복사해서 고치면 됩니다. 프로그램이 시작할 때 그 파일을 대신 읽고, 읽지 못하면 설정 창에 이유를 보여주고 내장 파일을 씁니다. 이 파일이 있으면 새 버전 exe의 내장 데이터보다 우선하므로, 새 릴리스에 반영된 뒤에는 지우세요.

## 실행

```powershell
dotnet run --project src/PoETradeSearch.App
dotnet test
```

전체 stat 데이터로도 테스트하려면 `/api/trade/data/stats` 응답을 저장하고 `POE_STATS_JSON` 환경 변수에 경로를 넣습니다.

## 배포용 exe 만들기

.NET이 설치되지 않은 PC에서도 실행되는 exe 하나(`publish/PoETradeSearch.exe`)를 만듭니다. 단일 파일 설정은 csproj에 들어 있습니다.

```powershell
dotnet publish src/PoETradeSearch.App -c Release -p:DebugType=none -o publish
```

- 압축은 꺼져 있습니다(약 140MB). 압축된 자체 풀림 exe는 백신이 의심하는 경우가 많아서입니다. 옮길 때는 zip으로 묶으면 됩니다.
- 거래소 데이터와 설정은 `%LOCALAPPDATA%\PoETradeSearch`에 저장됩니다. 로그 파일은 남기지 않습니다.

### GitHub 릴리스 자동 배포

`v`로 시작하는 태그를 푸시하면 GitHub Actions(`.github/workflows/release.yml`)가 테스트, exe 빌드를 거쳐 릴리스 페이지에 zip을 올립니다. 태그의 숫자가 exe 버전이 됩니다.

```powershell
git tag v0.6.0
git push origin v0.6.0
```

### 업데이트

프로그램이 켜질 때 GitHub의 최신 릴리스를 확인해서, 지금보다 새 버전이 있으면 설정 창 위에 "업데이트" 버튼이 나옵니다. 누르면 zip을 받아 GitHub에 기록된 SHA-256으로 확인한 뒤, 실행 중인 exe를 `PoETradeSearch.exe.old`로 이름을 바꾸고 새 exe를 그 자리에 놓고 다시 시작합니다. 새로 시작한 프로그램이 `.old`와 받은 파일을 지웁니다. 설정과 캐시(`%LOCALAPPDATA%\PoETradeSearch`)는 그대로입니다.

- exe가 쓰기 권한이 없는 폴더(예: Program Files)에 있으면 릴리스 페이지를 열어 직접 받게 합니다.
- 프리릴리스와 `v0.8.0-beta` 같은 태그는 업데이트로 보지 않습니다.
- 개발용: `--install-update`로 실행하면 새 버전을 찾자마자 업데이트 버튼을 누른 것처럼 동작합니다(낮은 버전으로 빌드해서 시험).

Actions 탭에서 "Release"를 직접 실행하면 릴리스 없이 빌드 결과만 아티팩트로 받을 수 있습니다.

### 개발용 실행 옵션

- `--item 파일`: 파일에 담긴 아이템 텍스트로 바로 검색 창을 엽니다.
- `--snapshot 폴더`: 메인 창, 검색 창, 실행 중 표시를 PNG로 저장하고 종료합니다(화면이 잠겨 있어도 동작).

## 아이콘

`src/PoETradeSearch.App/Assets/app.ico`, `app.png`는 `tools/make-icon.ps1`이 그립니다. 모양이나 색을 바꾸려면 스크립트를 고친 뒤 다시 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

## 문제가 생겼을 때

- 게임이 관리자 권한으로 실행 중이면 Windows가 이 프로그램의 키 입력을 막습니다. 메인 창에 경고와 "관리자 권한으로 다시 실행" 버튼이 나타납니다.
- 다운로드할 때 "의심되는 파일" 경고가 뜨는 것은 코드 서명이 없는 exe라서입니다. "추가 정보 → 실행"으로 실행할 수 있습니다.

## 진행 상황

- [x] 1단계: 단축키 → Ctrl+C → 클립보드 → 아이템 파서 (영문 클라이언트)
- [x] 2단계: 옵션 문장 → 거래소 stat ID 매칭 (`/api/trade/data/stats`)
- [x] 3단계: 검색 쿼리 생성 + `search` / `fetch` 호출, rate limit 준수
- [x] 4단계: 검색 창 (옵션 선택, 티어 범위로 최소/최대값, 가격 낮은 순 결과, 브라우저로 열기)
- [x] 5단계: 설정 (단축키, 서버, 리그), 한글(카카오) 클라이언트 지원, 게임 종료 시 같이 종료
- [x] 6단계: MVVM 구조, 게임 데이터 파일(game-data.json), 화면 크기 설정, 실행 중 표시
- [x] 7단계: GitHub 릴리스 자동 업데이트
