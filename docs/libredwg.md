# LibreDWG 사용 기록 — NexysDraft (draft3 갱신 2026-10-30)

## draft3: master 소스 빌드로 교체 (G0, 확정)
- 번들 = LibreDWG **master `33bfac15` (0.14.8679)**, MSYS2 mingw64 정적 빌드, 패치 없음. 빌드 기록 docs/libredwg-build.md, 해시 app/tools/libredwg/SHA256SUMS.txt.
- 교체 기준(run-draft3 G0): 사용자 도면 ODA 오류 0 ≥ 5/7 **그리고** u05 저장 ≤ 60 s → **6/7, u05 0.67 s로 충족**.

| 도면 | R2000 DXF | 0.14 (draft2 라이터) | master (draft2 라이터) | **master + draft3 라이터 (채택)** |
|---|---|---|---|---|
| u01 | 0.5 MB | FAIL 빈 출력 (2.5 s) | FAIL 빈 출력 (0.12 s) | **PASS 0.12 s** |
| u02 | 3.9 MB | FAIL 300 s 초과 | PASS 0.63 s | **PASS 0.64 s** |
| u03 | 10.1 MB | FAIL 300 s 초과 | PASS 1.7 s (스플라인 961/961) | **PASS 1.6 s** |
| u04 | 2.2 MB | PASS 126 s | PASS 0.33 s | **PASS 0.34 s** |
| u05 | 4.7 MB | FAIL 300 s 초과 | PASS 0.67 s (도면 영역 2/2) | **PASS 0.67 s** |
| u06 | 0.5 MB | FAIL BlockBegin owner (2.2 s) | FAIL Object improperly read | FAIL Object improperly read (IMAGE 1개) |
| u07 | 2.3 MB | FAIL BlockBegin owner (105 s) | FAIL 빈 출력 | **PASS 0.31 s** |
| 사용자 합계 | | **1/7** | 4/7 | **6/7** |
| g01·g02·g03·g06·selftest | | PASS | PASS | PASS |
| g04 | | FAIL 스플라인 1→0 | FAIL 스플라인 1→0 | 표본 매듭 수 오류 — 표본 수정 후 재측정(work/gates/f2_oda_dwg.txt) |
| g05 10만 엔티티 | 21.5 MB | 300 s 초과 | 6.4 s, ODA 읽기 정상 | — |

근거: work/gates/f2_oda_dwg_v014.txt, f2_oda_dwg_master.txt, f2_oda_dwg_master2.txt (ODA File Converter 27.1, 개발 PC 전용). 0.14 열의 시간은 같은 PC의 같은 시각 측정.
"PASS" = ODA 오류 0, 레이어·엔티티 개수 일치(라이터가 일부러 빼는 것 제외), TEXT/MTEXT 손실 0, 한글 100%, 도면 영역 보존.

### draft3에서 찾은 원인 (확정)
| # | 현상 | 원인 | 조치 |
|---|---|---|---|
| 9 | u01·u07 DWG를 ODA가 오류 없이 빈 도면으로 읽음(draft2 7b "미해결") | 원본의 **ByLayer 선종류 핸들이 `B`**. 엔티티를 전부 지워도 실패, LTYPE 항목을 지우면 통과, 핸들 B만 새 번호로 바꾸면 통과(9·29만 바꾸면 실패). 0.14에서도 같은 수정으로 u01 통과(2.5 s). draft2의 "LTYPE 재생성"은 원래 핸들을 유지했기 때문에 효과가 없었다 | R2000 라이터가 ByBlock·ByLayer·Continuous에 항상 새 핸들 발급(레이어는 이름으로 참조) |
| 10 | u06 "Object improperly read" | 엔티티 이분 탐색 → **IMAGE 1개**(핸들 5F, 이미지 정의 객체 없이 기록됨) | 사전 점검이 IMAGE를 "읽기 불가 예상"으로 표시, DXF 기본 |
| 11 | g04 스플라인이 ODA에서 사라짐 | 생성 표본이 잘못됨: 3차·제어점 5개인데 매듭 10개(9개여야 함). 법선·공차·플래그 변형은 무관 | 표본 수정, 사전 점검에 "매듭 수 오류 스플라인" 경고(비차단) |
| 12 | u04 DWG에 큰 테두리가 보임(nanoCAD 사용자 확인 a) | LibreDWG가 이름 없는 블록을 이름 "" 로 내보내고 INSERT도 "" 를 가리킴 | 리더가 `*NONAME~n`로 구분, R2000 라이터는 이름 없는 블록·해석 불가 삽입을 뺌 |

## 버전·출처 (draft2까지)
- GNU LibreDWG **0.14** win64 바이너리 (`libredwg-0.14-win64.zip`, SHA256 `1AD7E15344D20B3426C3435B078D82FB84B35062815946B2CCA9C5FC9810FEA8`, https://github.com/LibreDWG/libredwg/releases/tag/0.14). 0.14가 2026-10-30 기준 latest 릴리스.
- 업스트림 git(master 2026-10-29 `33bfac15`)에는 릴리스 뒤 수정이 계속 있으나 Windows 바이너리 아티팩트가 없고 CI(cmake·msvc·mingw·msys 잡)가 2026-10-22·10-29 모두 실패 상태. 개발 PC에 네이티브 gcc/MSYS2가 없어(NI Linux 크로스 컴파일러만 있음) 재빌드·패치는 이번 런에서 하지 않았다(blockers #8).
- 라이선스 GPLv3+. NexysDraft는 `app/tools/libredwg/`의 `dwg2dxf.exe`·`dxf2dwg.exe`를 **별도 프로세스**로만 실행한다(`NexysDraft.Core.Dwg.LibreDwg`). 링크·코드 복사 없음. 소스 포인터와 COPYING을 같은 폴더에 둔다(`SOURCE.txt`).
- 호출: 입력을 `%TEMP%\NexysDraft\libredwg\<id>\in.dwg|dxf`로 복사 → `dwg2dxf -y -o out.dxf in.dwg` / `dxf2dwg --as r2000 -o out.dwg in.dxf` → 결과 복사. 취소 토큰이 오면 프로세스를 죽인다(저장 취소).

## 지원 범위 (0.14 `--help` + 실측)
| 방향 | 지원 | 비고 |
|---|---|---|
| DWG → DXF (`dwg2dxf`) | 읽기 R13~R2018 | 사용자 샘플 7개(AC1015·AC1018·AC1024·AC1032) 모두 변환 성공, 0.1~1.5 s |
| DXF → DWG (`dxf2dwg`) | **R2000(기본)**·r12·r14·r2004 쓰기. r2007+ 미지원 | NexysDraft의 DWG 저장은 R2000 고정 |
| 인코딩 | DXF 문자열은 `$DWGCODEPAGE`(ANSI_949) 바이트로 출력(AC1024·AC1032 파일도) | 리더가 값마다 UTF-8 검증 후 코드페이지 폴백. NexysDraft의 DXF 출력은 순수 ASCII(`\U+XXXX`) |

## 검증 도구 (개발 PC 전용)
- ODA File Converter 27.1.0 (`C:\Program Files\ODA\ODAFileConverter 27.1.0\ODAFileConverter.exe`, config/dev.json, tools/oda.py, OdaGateTests). 제품·배포물·기본 셀프테스트는 참조하지 않는다.
- ODA는 **LibreDWG 0.14 `dxf2dwg`가 쓴 DWG를 기본 상태로는 하나도 읽지 못한다**("Object improperly read": g01·g03·g04·selftest, LibreDWG가 자기 DXF를 돌린 lib_u01도 "Invalid group code"). nanoCAD도 같은 파일을 RECOVER로만 열었다(사용자 확인, "AcDbLayout ModelType flag invalid").

## dxf2dwg 결함과 우회 (draft2에서 확정)
| # | 결함 | 근거 | 우회 (R2000 라이터) |
|---|---|---|---|
| 1 | OBJECTS에 LAYOUT·PLOTSETTINGS·MLINESTYLE·ACDBPLACEHOLDER·사전을 쓰면 DWG의 객체가 깨진다 | work/oda/exp: base·varA(플롯 플래그 수정)·varD(레이아웃 제거) 모두 "Object improperly read"; varB/C(OBJECTS 제거)는 "Null object Id" | **OBJECTS = 루트 DICTIONARY 하나**, BLOCK_RECORD의 340 레이아웃 링크 생략(varE) → ODA 오류 0. dxf2dwg가 레이아웃을 스스로 만든다 |
| 2 | 소스 파일의 낮은 핸들(예: `*Paper_Space` ENDBLK = 1F)이 dxf2dwg 내부 예약 핸들과 충돌("Duplicate handle 1F for object 47 already points to object 0") → ODA가 0바이트 파일을 내놓음(u01·u06·u07) | work/gates/f2_oda_dwg.txt 1차 | 모든 BLOCK/ENDBLK 엔티티에 새(높은) 핸들 발급, HANDSEED는 파일 안 모든 5/105 코드 최대값+1 |
| 3 | 엣지형(선·호·타원·스플라인 엣지) HATCH 경계를 쓰지 않아 ODA/nanoCAD에서 해치가 사라진다(DWG JSON에 paths 없음) | work/oda/exp varE JSON, g03 | 엣지 루프를 테셀레이션한 폴리라인 루프(92=2, 72=0, 73=1)로 쓴다 — 폴리라인·타원 엣지는 원래 살아남음(g01·g04) |
| 4 | MTEXT text_height를 0으로 기록(자기 DXF 왕복에서도) | dwgread JSON | 문자열 앞 `\H<높이>;` 절대 높이 코드 |
| 5 | MTEXT 코드 50 거부 | "Invalid DXF code 50 for MTEXT" | 11/21/31 X축 벡터로 |
| 6 | BLOCK_RECORD 핸들 0 → 블록 소실 | "Unknown INSERT.block_header" | 핸들 0 → 새 핸들 |
| 7 | 속도(R2000 DXF 크기 기준): 0.53 MB 2.4 s, 2.38 MB 38~59 s, 2.80 MB 79~125 s, 4.13 MB 128~205 s, u05(≈5 MB)·u03·10만 엔티티 300 s 초과 | d1b/f2 관문 | 백그라운드 저장 + 경과/예상(≈1+12·MB²) + 취소. u03·u05·10만 엔티티는 DWG 관문에서 SKIP/타임아웃 |
| 7a | HATCH에 경계 원본 핸들(97 n + 330…)을 쓰면 "Unknown DXF code 330 for HATCH" → 객체 손상 | work/oda/exp5 v4b.log | R2000 출력은 97 0, 71(associative) 0 |
| 7b | 사용자 도면(u01·u07)의 DWG는 ODA가 **오류 메시지 없이 0바이트**를 내놓는다. 엔티티 유형별 제거(11종)·테이블 재생성(LTYPE·STYLE·LAYER·VPORT)으로도 변화 없음(r2004/r14 출력은 미시험); LibreDWG 자신의 DXF→DWG(lib_u01)도 같은 결과 | work/oda/exp7·exp8·exp9 | ~~미해결~~ → **draft3에서 해결(#9)**: 원인은 ByLayer 선종류 핸들 `B`. draft2의 "dxf2dwg 0.14 자체 결함" 추정은 틀렸다 |
| 8 | u06(동적 블록·`*U` 표현 블록, AcAecLayerStandard xdata)은 OBJECTS 최소화·xdata 제거 후에도 "BlockBegin owner Id is invalid"·"Object improperly read" | work/oda/exp4 | 미해결(UNVERIFIED 원인) — R2000 손실 목록에 동적 블록 경고, DXF 저장 권장 |

## 그 밖에 알게 된 것
- 리더 결함(draft1): HATCH 루프를 파싱만 하고 붙이지 않아 파일에서 읽은 해치는 전부 경계 0개였다(nanoCAD "해치 미표시"의 절반, ODA HATCH N/0). draft2에서 수정.
- ODA는 DIMENSION의 `*D` 블록을 자기 식으로 재생성하고(LINE 18·MTEXT 4·POINT 12), 치수가 있으면 Defpoints 레이어를 만든다 — 관문 비교에서 제외.
- ODA는 MTEXT 줄 끝 공백을 지우고 `\S1^2;`를 `\S1<U+FFF2>;`로 다시 쓴다 — 한글 비교는 글자·숫자만 남겨 비교.

## draft4 (2026-10-30) — xdata APPID

| # | 현상 | 근거 | 우회 |
|---|---|---|---|
| 13 | dxf2dwg(master 33bfac15)가 `1001 ACAD` xdata의 APPID 참조를 DWG에 항상 핸들 0x12로 쓴다. ACAD 외 앱 이름은 0 | dwgread JSON eed handle `[5,1,18]` / `[..,0]`, ODA `_APP12` / `_APP0` (u01·u04·u07, 표 순서·핸들을 바꿔도 같음) | R2000 라이터가 ACAD APPID에 핸들 12를 준다. 12가 쓰이면 경고. ACAD 외 xdata는 끊긴 채 남음(읽기 오류 아님) |
