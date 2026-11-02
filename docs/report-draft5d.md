# NexysDraft draft5d 보고서 — 저장한 DXF·DWG가 AutoCAD에서 안 열림 → v0.4.3

2026-11-02. 동료 보고: NexysDraft에서 DWG든 DXF든 저장한 뒤 AutoCAD로 열면 오류가 나고 열리지 않는다.

## 원인 (확정)
개발 PC에는 AutoCAD가 없다. AutoCAD 대신 ezdxf 1.4.5(recover + audit)와 직접 만든 구조 검사로 저장 파일을 점검했다.

1. **빈 `{ACAD_XDICTIONARY }` 묶음**: `DxfWriter.Common`의 `g.Code == 102 || _in102(g)`가 단축 평가돼서, 102 괄호는 쓰이고 그 안의 360 포인터는 빠졌다.
   - 다시 생성한 엔티티 가운데 원본에 확장 사전이 있던 것(주로 MTEXT)에서 생긴다.
   - R2000 DXF 저장 7개 중 5개(u01·u02·u03·u06·u07)를 ezdxf가 아예 읽지 못했다("ACAD_XDICTIONARY error").
   - **DWG 저장도 이 R2000 DXF를 dxf2dwg에 넘기므로 같은 문제를 안고 있었다.**
2. **DWG에서 연 도면을 "원본 버전 유지"로 저장**: LibreDWG dwg2dxf 출력의 OBJECTS와 테이블 레코드를 그대로 옮겨 썼다. 그 출력 자체가 AutoCAD 규칙에 맞지 않는다.
   - u04: 중복 핸들 5,210개, 끊어진 330/340/350/360 포인터 376개, 이름 없는 BLOCK_RECORD 4개
   - u01: 루트 사전이 OBJECTS의 첫 객체가 아니고, 중복 핸들 2개
3. R2000에서 u04의 이름 없는 DIMSTYLE이 그대로 쓰였다. u06의 IMAGE는 쓰지 않은 IMAGEDEF를 가리켰다.

## 수정
- 102 묶음: R2000에서는 통째로 뺀다(가리키는 객체를 쓰지 않으므로). 원본 유지 모드에서는 내용까지 온전히 쓴다.
- DWG에서 연 도면의 DXF 기본 저장은 항상 **R2000으로 새로 생성**한다(`Document.FromLibreDwg`, `DxfSave.Choose`).
- DXF로 직접 저장할 때는 AutoCAD식 OBJECTS 골격(그룹·레이아웃·플롯 설정·MLINESTYLE 사전)을 쓴다. dxf2dwg로 넘기는 DXF는 기존처럼 루트 사전만 쓴다(draft2에서 dxf2dwg가 거부한 구성이라).
- R2000: 이름 없는 테이블 레코드를 빼고, IMAGE는 빼면서 경고를 남긴다("n IMAGE left out"). 원본을 그대로 옮기는 엔티티의 102 묶음도 뺀다.

## 관문
| 항목 | 결과 |
|---|---|
| 새 관문 `Gate_D5d_SavedDxf_AutoCadStructure`: 사용자 도면 7개 + 생성 샘플 6개 × (DXF 기본 저장, DWG용 R2000 DXF) | **13/13 PASS**. 구조 위반 0, ODA 판독 오류 0 (work/gates/d5d_autocad_structure.txt) |
| 대조군: 검사기가 LibreDWG 원본 출력(u04)의 중복 핸들을 잡는지 | PASS |
| ezdxf recover + audit, 사용자 도면 7개 DXF 기본 저장 | 수정 0 · 오류 0 (work/gates/d5d_ezdxf_audit.txt). 수정 전에는 5개를 읽지 못함 |
| dotnet test | 39/39 |
| --selftest | PASS 40 · FAIL 0 |
| zip 스모크 | work/gates/i4d_smoke.txt |

## 확인하지 못한 것
- **실제 AutoCAD에서 열리는지는 확인하지 못했다.** AutoCAD가 없어서, 알려진 규칙을 검사한 결과다.
- **DWG**는 LibreDWG dxf2dwg가 쓴다. 넘기는 DXF의 결함은 고쳤지만, AutoCAD가 LibreDWG가 쓴 DWG 자체를 받아들이는지는 알 수 없다.
  - LibreDWG 문서도 DWG 쓰기를 실험 단계로 둔다.
  - AutoCAD에서 계속 안 열리면 DWG 대신 DXF로 저장하는 것을 권한다.

## §사용자 확인
1. v0.4.3으로 DWG를 열어 **DXF로 저장**한 파일이 AutoCAD에서 열리는지.
2. 같은 도면을 **DWG로 저장**한 파일이 AutoCAD에서 열리는지.
3. 안 열리면 AutoCAD 명령줄의 오류 문구(예: "Invalid or incomplete DXF input -- drawing discarded" 다음 줄의 섹션·줄 번호)와 AutoCAD 버전.
