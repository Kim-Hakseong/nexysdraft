# NexysDraft draft5c 보고서 — 헤더 로고를 Nexys 제품군과 통일 → v0.4.2

2026-10-31. 기준은 v0.4.1(0cc198a)이다. 사용자 피드백: 앱 화면 왼쪽 위 헤더 로고가 Nexys VIEW·NexysBus와 다르다(draft1의 자리표시자 "N" 원).

## 기준으로 쓴 파일
- 브랜드 원본: `C:\rea-work\diadem\design\brand\Logo.png`(워드마크 "NXS NEXYS / NEW EXPERIMENT SYSTEM"), `AppIcon.png`
- Nexys VIEW 헤더: `C:\rea-work\diadem\app\web\src\main.ts`(`.hdr-logo` 안의 `<img src="brand/Logo.png">`), `styles.css` 35–38행(`.hdr-logo` 높이 22 px, 이미지 18 px, 다크 테마는 `filter: brightness(1.15)`), 자산은 `app\web\public\brand\Logo.png`
- NexysBus(`C:\rea-work\nexys-bus`), `C:\rea-work\nexys-view`도 대조했다. Logo.png·AppIcon.png·AppIcon.ico의 MD5와 헤더 CSS가 세 제품 모두 같다. 다른 점은 이름 줄 하나다. VIEW는 로고 옆에 버전만 쓰고, Bus는 로고 + 굵은 제품명 + 버전 줄이다.

## 적용
- `app\web\public\brand\Logo.png`는 원본을 그대로 복사했다(MD5 2e4a3722…). 경로·파일명은 VIEW와 같다.
- 헤더 마크업과 CSS(`.hdr-brand` 최소 폭 220 px, `.hdr-logo`, 다크 밝기 보정)를 VIEW와 같게 맞췄다. 자리표시자 "N" 원(accent 배경)은 없앴다.
- 이름 줄은 지시서의 예(로고 + "NexysDraft" + 버전 줄)를 따랐다. 이것은 NexysBus 방식이고, VIEW에는 제품명 줄이 없다. 로고 영역 자체는 VIEW와 똑같다.
- 창 아이콘과 작업표시줄 아이콘: `Assets\brand\AppIcon.ico`(MD5 b2a3abd9…)와 `AppIcon.png`(91d77b77…)가 VIEW·Bus와 같은 파일이라 손대지 않았다.

## 관문
| 항목 | 결과 |
|---|---|
| 로고 영역(페이지 8,6–168,46 px) 픽셀 상관, 라이트 | **1.00**(mean abs diff 0.0), docs/screens/50_header_light.png(위 VIEW, 아래 NexysDraft) |
| 같은 대조, 다크 | **1.00**(0.0), docs/screens/51_header_dark.png |
| 대조군: v0.4.0 헤더("N" 원)와 비교 | 0.21 — 관문이 차이를 잡아낸다 |
| dotnet test | 37/37 |
| --selftest | PASS 40 · FAIL 0. F10 실제 키 관문은 창이 전경을 얻지 못해 SKIP |
| zip 스모크 | selftest·skilltest PASS, PDF u05 12 + u01 1, zip 사본 캡처의 로고 상관 1.00 (work/gates/i4c_smoke.txt) |
| 패키지 | `NexysDraft-v0.4.2-win64.zip` 96,718,535 B, SHA256 `FBA58B3A2449767C8C9C6B80F850E0D647B4A951D8A3033138447EBECB474791` |

수치는 work/gates/d5c_header_logo.txt에 있다. 대조 도구는 `tools\pixel_diff.py --logo A.png B.png 출력.png [x0,y0,x1,y1]`(Python312)이다. app\dist는 건드리지 않았다.
