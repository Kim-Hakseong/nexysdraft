# LibreDWG 사용 기록 — NexysDraft (draft2 갱신 2026-10-30)

## 버전·출처
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
| 7 | 속도: 200 KB 2 s, 600 KB 95~109 s, 1.0 MB 278 s, 1.7 MB·21 MB 300 s 초과 | d1b/f2 관문 | 백그라운드 저장 + 경과/예상(≈2+250·MB²) + 취소. u03·10만 엔티티는 DWG 관문에서 SKIP |
| 8 | u06(동적 블록·`*U` 표현 블록, AcAecLayerStandard xdata)은 OBJECTS 최소화·xdata 제거 후에도 "BlockBegin owner Id is invalid"·"Object improperly read" | work/oda/exp4 | 미해결(UNVERIFIED 원인) — R2000 손실 목록에 동적 블록 경고, DXF 저장 권장 |

## 그 밖에 알게 된 것
- 리더 결함(draft1): HATCH 루프를 파싱만 하고 붙이지 않아 파일에서 읽은 해치는 전부 경계 0개였다(nanoCAD "해치 미표시"의 절반, ODA HATCH N/0). draft2에서 수정.
- ODA는 DIMENSION의 `*D` 블록을 자기 식으로 재생성하고(LINE 18·MTEXT 4·POINT 12), 치수가 있으면 Defpoints 레이어를 만든다 — 관문 비교에서 제외.
- ODA는 MTEXT 줄 끝 공백을 지우고 `\S1^2;`를 `\S1<U+FFF2>;`로 다시 쓴다 — 한글 비교는 글자·숫자만 남겨 비교.
