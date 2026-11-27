# PoE Trade Search

Path of Exile 1용 거래소 검색 도구 (C# / WPF, .NET 11).

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

## 진행 상황

- [x] 1단계: 단축키 → Ctrl+C → 클립보드 → 아이템 파서 (영문 클라이언트)
- [ ] 2단계: 옵션 문장 → 거래소 stat ID 매칭 (`/api/trade/data/stats`)
- [ ] 3단계: 검색 쿼리 생성 + `search` / `fetch` 호출, rate limit 준수
- [ ] 4단계: 게임 위 오버레이 UI (옵션 선택, 최소값 조절, 브라우저로 열기)
- [ ] 5단계: 설정 (리그, 단축키), 트레이 아이콘, 한글(카카오) 클라이언트 지원
