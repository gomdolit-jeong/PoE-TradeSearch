# PoE Trade Search

Path of Exile 1용 거래소 검색 도구 (C# / WPF, .NET 11).

영문(글로벌) 클라이언트와 한글(카카오) 클라이언트를 모두 지원합니다. 복사한 아이템이 한글이면 카카오 거래소(poe.kakaogames.com), 영문이면 글로벌 거래소(pathofexile.com)로 자동으로 검색합니다. 두 거래소의 매물은 같고 표시 언어만 다릅니다.

게임 중 아이템에 마우스를 올리고 단축키(기본 `Ctrl+D`)를 누르면 Ctrl+C를 한 번 보내 아이템 텍스트를 복사하고,
옵션을 파싱해서 거래소에서 비슷한 아이템을 검색합니다.

## 구조

| 프로젝트 | 역할 |
|---|---|
| `src/PoETradeSearch.Core` | UI와 무관한 로직: 아이템 텍스트 파서, (예정) stat 매칭, 거래소 API 클라이언트 |
| `src/PoETradeSearch.App` | WPF 앱: 전역 단축키, Ctrl+C 전송, 클립보드, 화면 |
| `tests/PoETradeSearch.Tests` | xUnit 테스트 |

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

### 개발용 실행 옵션

- `--item 파일`: 파일에 담긴 아이템 텍스트로 바로 검색 창을 엽니다.
- `--snapshot 폴더`: 메인 창과 검색 창을 PNG로 저장하고 종료합니다(화면이 잠겨 있어도 동작).

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
