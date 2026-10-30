# LibreDWG 재빌드 기록 (draft3 G0, 2026-10-30)

## 결과 요약
- MSYS2(winget `MSYS2.MSYS2`, `C:\msys64`) + mingw-w64-x86_64-gcc로 LibreDWG **master `33bfac155464cd560f41537ae627efbd66ed7348` (`git describe` 0.14.8679)** 을 **1회차에 빌드 성공**.
- 산출물(`work/g0/out_master` → 교체 후 `app/tools/libredwg`): `dwg2dxf.exe` 15 MB, `dxf2dwg.exe` 11 MB, `dwgread.exe` 18 MB(정적 링크), `libiconv-2.dll`. 0.14 배포본의 `libredwg-0.dll`·pcre2 DLL은 필요 없음.
- 패치 없음(`patches/` 비어 있음). 소스는 업스트림 그대로, 빌드 스크립트만 추가.
- dxf2dwg의 쓰기 버전은 master에서도 r12·r14·r2000·r2004만. NexysDraft는 계속 R2000.

## 시도 기록
| # | 시각 | 단계 | 결과 | 근거 |
|---|---|---|---|---|
| 1 | 10-30 오전 | winget으로 MSYS2 설치 | 성공. 기본 미러가 느려 `/etc/pacman.d/mirrorlist.*`를 mirror.msys2.org·repo.msys2.org로 고정(원본 `*.bak`) | C:\msys64\etc\pacman.d |
| 2 | 10-30 | `pacman -S git base-devel autoconf automake libtool make texinfo mingw-w64-x86_64-gcc mingw-w64-x86_64-libiconv mingw-w64-x86_64-pcre2 python` | 성공 | work/g0/run1.sh, work/g0/build1.log |
| 3 | 10-30 | `tools/build-libredwg.sh master …` (autogen → configure `--disable-bindings --disable-python --disable-docs --disable-shared --enable-static --disable-werror` → `make -C src` → `make -C programs`) | **BUILD OK**, EXIT 0 | work/g0/build1.log 끝 |
| 4 | 10-30 | ODA 관문(F2/G0) master vs 0.14 | 표는 docs/libredwg.md §draft3 | work/gates/f2_oda_dwg_master.txt, f2_oda_dwg_v014.txt |
| 5 | 10-30 | 실패 원인 이분 탐색(u01) | 엔티티 전부 제거해도 실패 → 테이블 단위 제거 → LTYPE 항목 제거 시 통과 → 핸들 하나씩 바꿔 보니 **ByLayer 선종류 핸들 `B`** 가 원인 | tools/bisect_sections.py, work/g0/rehandle.py, work/g0/rehandle_u01 |
| 6 | 10-30 | 같은 수정으로 u07 통과, u06은 IMAGE 1개가 원인으로 남음 | u07 `remap only B → True`, u06 이분 탐색 결과 `IMAGE 5F` | work/g0/rehandle_u07_kj_pid, work/g0/bisect_u06 |
| 7 | 10-30 | g04 SPLINE 1→0 | 표본의 매듭 수 오류(제어점 5·3차인데 매듭 10개, 9개여야 함). ODA가 버림. 표본 수정 | work/g0/spline_g04, Samples.cs |

## 재현 방법
```
# MSYS2 MINGW64 셸
pacman -S --needed git base-devel autoconf automake libtool make texinfo mingw-w64-x86_64-gcc mingw-w64-x86_64-libiconv mingw-w64-x86_64-pcre2 python
bash tools/build-libredwg.sh 33bfac155464cd560f41537ae627efbd66ed7348 /c/src/libredwg /c/out/libredwg
```
PowerShell에서는 `tools\build-libredwg.ps1`이 위 과정을 `C:\msys64\usr\bin\bash.exe -lc`로 실행한다.

## 라이선스
GPLv3+. 바이너리 옆에 `COPYING`, `SOURCE.txt`(업스트림 URL·커밋), `build-libredwg.sh`를 함께 배포한다. 링크·코드 복사 없음(별도 프로세스 호출).
