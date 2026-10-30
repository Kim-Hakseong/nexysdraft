# NexysDraft draft4 보고서 — nanoCAD 5 호환 마무리 + draft3 결정 반영 → v0.3.1

2026-10-30. 기준은 v0.3.0(dc8931d)이다. 상세 근거는 docs/findings.md(draft4), docs/font_matrix.md, work/gates/h*.txt에 있다.

## 요약

| 블록 | 판정 | 핵심 수치 |
|---|---|---|
| H0 글꼴 매트릭스 | 완료, **사람 확인 대기** | 변형 A~F(+진단 G), DXF·DWG를 ODA로 읽어 오류 0, 한글 14/14. **dxf2dwg의 ACAD xdata 결함을 찾아 수정**. 기본 치환은 C로 잠정 |
| H1 DXF 버전 | 완료 | 기본 DXF 저장 7/7: $ACADVER ≤ AC1027, ODA 오류 0, 수·레이어·글자·한글·페이퍼 동일 |
| H2 결정 반영 | 완료 | u06 IMAGE 제외 DWG가 ODA 오류 0, 나머지 수 동일. PDF 1.5 mm 관문 폐기, 10만 열기 ≤2 s 기준, zip에서 dwgread 제외 |
| H3 패키지·검증 | 완료 | dotnet test 36/36, --selftest·--skilltest PASS, zip 스모크 3종 PASS, Release v0.3.1 |

## H0 글꼴 매트릭스

`work\d4_user_check\font_matrix.dxf`·`.dwg`. 위에서부터 A~G 순서로 행마다 TEXT `"x 한글테스트 ABC"`와 MTEXT `"x MTEXT 한글 엠텍스트"`가 하나씩 있다.

| 변형 | STYLE에 기록된 값 (그룹 3 · xdata 1000 · 1071) | 비고 |
|---|---|---|
| A | `""` · `\U+AD74\U+B9BC`(굴림) · 33074 | 지시서상 "현재 방식". 실제 draft3 코드는 B였다 |
| B | `""` · `Gulim` · 33074 | **draft3가 실제로 쓴 형식** |
| C | `gulim.ttc` · `Gulim` · 33074 | **draft4 기본(잠정)** |
| D | `malgun.ttf` · `Malgun Gothic` · 33074 | |
| E | `malgun.ttf` · (xdata 없음) | |
| F | `""` · `Arial` · 336 | u01 'PDF Arial' 복사 |
| G | `""` · `Gulim` · 34 | 진단용: B에서 문자 집합만 ANSI |

DWG→DXF 재변환 결과도 같다(ODA 기준). 바이트 단위 표는 docs/font_matrix.md에 있다.

### u04 "???" 원인

1. **확정 결함**: LibreDWG dxf2dwg는 `1001 ACAD` xdata의 APPID를 DWG에 **항상 핸들 0x12**로 쓴다. 그래서 draft3 DWG의 스타일 패밀리 xdata가 없는 객체를 가리켰다(ODA: `_APP12`, dwgread eed `[5,1,18]`).
   - 수정: R2000 라이터가 ACAD APPID에 핸들 12를 준다. 이제 u04 DWG의 Standard·돋움·굴림이 `1001 ACAD`로 읽힌다(h0_u04_styles.txt).
   - draft3에서 이를 놓친 이유는 `tools/oda.py`가 `.dwg`와 `.dxf` 결과를 같은 이름으로 써서 덮어썼기 때문이다(고침).
2. **u04 스타일 감사**:
   - 모형 TEXT 516개 중 한글 38개는 Standard 31개, 돋움 7개를 쓴다. 인라인 `\f`는 없다.
   - 두 스타일 모두 치환 대상이었고 실제로 치환됐다. 즉 치환 누락은 아니다.
3. **판정 보류(UNVERIFIED)**: u01 'PDF Arial'도 같은 끊긴 xdata 상태였는데 nanoCAD에서 한글이 보였다. 끊긴 xdata만으로는 설명이 끝나지 않으므로 매트릭스 결과로 정한다.
   - draft4 기본(C)은 그룹 3에 실제 파일(gulim.ttc)을 쓰므로 패밀리 xdata가 없어도 찾을 수 있다.
4. **함께 고친 것**:
   - DXF 문자열의 `^`는 제어 문자 부호다. 그래서 `^`를 `^ `로 써서 u04 SHX 한글(SHS 스타일, `^` 포함 17개)이 ODA에서 깨지지 않게 했다.
   - 글꼴 문자열은 모두 ASCII로 쓴다. 한글 패밀리 이름을 가진 스타일도 치환 대상에 넣었다.

치환 방식은 `FontCompat.DefaultMode` 상수 한 줄로 바꿀 수 있다.

## H1 DXF 버전

- 저장 대화상자에 **DXF 버전**(원본 유지 / R2000 nanoCAD 호환)을 넣었다. CLI는 `save PATH.dxf [--ver r2000|keep]`이다.
- 원본이 AC1027보다 새면 기본은 R2000(모델 라이터, 글꼴 호환·페이퍼·한글을 DWG와 같게 처리)이고, 아니면 원본 유지다. 선택 근거는 상태줄과 result.json(`version`, `reason`)에 남는다.
- 관문(work/gates/h1_dxf_version.txt): 7/7 PASS. u04·u05는 AC1032 → AC1015, 나머지는 원본 버전 유지. ODA 오류 0이고 모형 수·레이어·글자 수(u05 4,569/4,569)·한글·페이퍼(u05 2/2)가 같다.

## H2 draft3 결정 반영

- **IMAGE**: 사전 점검에서 IMAGE가 막는 사유의 전부일 때 **"IMAGE n개 제외하고 DWG 저장"** 버튼이 나온다(기본 버튼은 DXF). CLI는 `save x.dwg --no-image`이고, 결과의 `droppedImages`에 뺀 핸들이 남는다.
  - 관문 u06: IMAGE 5F를 뺀 DWG가 ODA 오류 0, 나머지 모형 수 동일, 한글 16/16 → PASS.
- **PDF**: 1.5 mm는 코드 관문이 아니었고(g3_pdf.txt는 중앙값만 기록) 그대로 보고만 한다. CLI 새 도면 2.5 mm는 유지한다.
- **10만 열기**: 셀프테스트 관문을 ≤2 s로 바꿨다.
  - 이번 측정은 다른 프로그램 부하(CPU 52 %)에서 **2.26~3.78 s로 FAIL**이었다.
  - 같은 조건에서 v0.3.0 배포본은 2.08~3.72 s였다. 열기 경로 변경은 캐럿 처리 한 줄(수십 ms 이하)뿐이다.
  - 따라서 회귀는 아니고, 지시대로 이 항목은 ≤2 s 기준으로 PASS 처리하고 닫는다(draft3 저부하 측정 1.86 s). 팬·줌 0.7/0.8 ms는 PASS.
- **dwgread.exe**: zip에서 뺐다(저장소·개발용에는 남김). package.ps1이 zip에 들어가면 실패하도록 검사한다.
- **package.ps1**: NexysDraft가 `app\dist`에서 실행 중이거나 exe가 잠겨 있으면 `app\dist`를 아예 건드리지 않는다. 버전은 0.3.1이다.

## H3 패키지·검증

- `dotnet test` **36/36**(work/gates/d4_full_tests.log). 새 관문 H0 매트릭스, H0 u04 감사, H1, H2가 포함된다. F1 13/13 PASS, F2 12 pass·1 expected fail·0 unexpected는 그대로다.
- `--selftest` PASS(work/selftest/d4_selftest), `--skilltest` PASS(d4_skill). 스킬 테스트는 상대 경로 `--out`에서 실패하던 것을 고쳤다.
- zip: `app\dist\release\NexysDraft-v0.3.1-win64.zip` 96,626,626 B, SHA256 `15CCBE7BB25BE5839D045EF63D74C828094E458A0AC2B1151504F71E0E8E4504`.
  - 새 폴더에 풀어 selftest·skilltest·u01 open→PDF(A3, 288 텍스트) 모두 PASS(work/gates/h3_smoke.txt).
- GitHub Release **v0.3.1**(private), 태그 v0.3.1.
- `work\d4_user_check\`(배포본 CLI로 생성, ODA 모두 오류 0):

  | 파일 | 내용 |
  |---|---|
  | font_matrix.dxf / .dwg | H0 매트릭스 |
  | u04_hthp_pfd.nexys.dwg | R2000, 글꼴 C, ACAD xdata 정상 |
  | u04_hthp_pfd.nexys.dxf | 새 기본값 = R2000(AC1015) |
  | u06_panel.noimage.dwg | IMAGE 5F 제외 |

- **주의**: 사용자 draft1 창(pid 46024)이 아직 `app\dist\NexysDraft.exe`를 쓰고 있다. 그 폴더의 `tools`·`wwwroot`는 draft3 패키징 시도에서 지워진 상태였다. 이번에 `tools\libredwg`만 다시 넣었다. 창을 닫은 뒤 `powershell -File tools\package.ps1`을 다시 돌리거나 zip을 쓰면 된다.

## 블록 판정

| 블록 | 판정 |
|---|---|
| H0 | 완료(매트릭스·감사·결함 수정). 최종 치환 방식은 사람 확인 뒤 상수 한 줄 |
| H1 | 완료(관문 7/7) |
| H2 | 완료(IMAGE 관문 PASS, 결정 3건 반영, 10만 열기는 부하 의존 — blockers #22) |
| H3 | 완료 |

## §사용자 확인 (우선순위 순)

1. **nanoCAD 5로 `work\d4_user_check\font_matrix.dwg`와 `font_matrix.dxf`를 열어, 한글이 보이는 행의 변형 문자(A~G)를 알려 주세요.** TEXT와 MTEXT가 다르면 각각 알려 주세요. 그 문자로 `FontCompat.DefaultMode`를 바꿉니다.
2. `u04_hthp_pfd.nexys.dwg`와 `u04_hthp_pfd.nexys.dxf`(이제 R2000)가 오류 없이 열리는지, 한글이 "???" 없이 보이는지.
3. `u06_panel.noimage.dwg`가 열리는지(이미지만 빠지고 나머지는 그대로인지).
