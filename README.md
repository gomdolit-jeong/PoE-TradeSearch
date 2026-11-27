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

.NET이 설치되지 않은 PC에서도 실행되는 exe 하나(`publish/PoETradeSearch.exe`)를 만듭니다.

```powershell
dotnet publish src/PoETradeSearch.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o publish
```

`IncludeAllContentForSelfExtract`가 없으면 WPF의 `DirectWriteForwarder.dll`을 못 읽어서 실행하자마자 종료됩니다.

거래소 stat 데이터는 `%LOCALAPPDATA%\PoETradeSearch\stats.json`에 하루 동안 캐시됩니다.

## 아이콘

`src/PoETradeSearch.App/Assets/app.ico`, `app.png`는 `tools/make-icon.ps1`이 그립니다. 모양이나 색을 바꾸려면 스크립트를 고친 뒤 다시 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
```

## 문제가 생겼을 때

- 로그는 `%LOCALAPPDATA%\PoETradeSearch\logs\log-날짜.txt`에 남습니다. 단축키 입력, 앞에 있던 창, Ctrl+C 전송 결과, 클립보드 응답 시간, 검색 요청이 기록됩니다.
- 메인 창의 **진단 정보 복사** 버튼은 환경 정보와 최근 로그를 한 번에 복사합니다.
- 게임이 관리자 권한으로 실행 중이면 Windows가 이 프로그램의 Ctrl+C를 막습니다. 메인 창에 경고와 "관리자 권한으로 다시 실행" 버튼이 나타납니다.

## 진행 상황

- [x] 1단계: 단축키 → Ctrl+C → 클립보드 → 아이템 파서 (영문 클라이언트)
- [x] 2단계: 옵션 문장 → 거래소 stat ID 매칭 (`/api/trade/data/stats`)
- [x] 3단계: 검색 쿼리 생성 + `search` / `fetch` 호출, rate limit 준수
- [x] 4단계: 검색 창 (옵션 선택, 최소/최대값 입력, 가격 낮은 순 결과, 브라우저로 열기, 귓속말 복사)
- [ ] 5단계: 설정 (리그, 단축키), 트레이 아이콘, 한글(카카오) 클라이언트 지원
