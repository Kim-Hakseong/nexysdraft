# 글꼴 매트릭스 (draft4 H0) — nanoCAD 5에서 한글이 보이는 STYLE 형식 찾기

파일: `work\font_matrix\font_matrix.dxf`(R2000, 우리 R2000 라이터)와 `font_matrix.dwg`(같은 DXF → LibreDWG dxf2dwg R2000).
사용자 확인용 사본은 `work\d4_user_check\`에 있다. 만드는 코드는 `app/NexysDraft.Tests/H0Tests.cs`(`Gate_H0_FontMatrix`)이고, 관문 결과는 `work/gates/h0_font_matrix.txt`에 있다.

각 행에는 스타일 `FM_x` 하나와 그 스타일로 쓴 TEXT `"x 한글테스트 ABC"`(높이 10), MTEXT `"x MTEXT 한글 엠텍스트"`가 하나씩 있다. 행은 위에서부터 A, B, C… 순서로 20씩 아래로 내려간다.

## 변형과 실제로 기록된 값

| 변형 | 뜻 | DXF에 쓴 STYLE (그룹 3 · 4 · 1001 · 1000 · 1071) | DWG를 ODA로 읽은 STYLE | DWG를 LibreDWG dwg2dxf로 읽은 STYLE |
|---|---|---|---|---|
| A | 3 비움 + xdata 한글 패밀리 | `""` · `""` · ACAD · `\U+AD74\U+B9BC`(굴림) · 33074 | 같음(1000 = UTF-8 "굴림") | 같음 |
| B | 3 비움 + xdata `Gulim` (**draft3 방식**) | `""` · `""` · ACAD · `Gulim` · 33074 | 같음 | 같음 |
| C | 3 `gulim.ttc` + xdata `Gulim` (**draft4 기본, 잠정**) | `gulim.ttc` · `""` · ACAD · `Gulim` · 33074 | 같음 | 같음 |
| D | 3 `malgun.ttf` + xdata `Malgun Gothic` | `malgun.ttf` · `""` · ACAD · `Malgun Gothic` · 33074 | 같음 | 앞에 `ACAD malgun 34`가 하나 더 붙음(dwg2dxf가 만든 것) |
| E | 3 `malgun.ttf`, xdata 없음 | `malgun.ttf` · `""` | 같음(xdata 없음) | `ACAD malgun 34`가 붙음(dwg2dxf가 만든 것) |
| F | u01 'PDF Arial' 그대로 | `""` · `""` · ACAD · `Arial` · 336 | 같음 | 같음 |
| G | B에서 1071만 34(ANSI 문자 집합)로 바꾼 진단용 | `""` · `""` · ACAD · `Gulim` · 34 | 같음 | 같음 |

- F의 원본은 u01 원본 DXF(dwg2dxf)의 'PDF Arial'이다. 그룹 3 `""`, 패밀리 `Arial`, 1071 = 336.
- 지시서는 A를 "현재 방식(\U+ 이스케이프)"으로 적었지만, draft3 코드가 실제로 쓴 것은 **B**(ASCII `Gulim`)다(`FontCompat.MapFamily`가 영문 이름을 돌려줌). A는 비교용으로 따로 만들었다.
- ODA 읽기: `.dxf`와 `.dwg` 모두 오류 0이고, 한글 TEXT·MTEXT 14개가 모두 남았다. xdata도 모두 ACAD APPID에 붙어 있다(`_APPn` 0개).

## 매트릭스를 만들다 찾은 결함: ACAD xdata가 DWG에서 끊긴다 (확정)

- LibreDWG dxf2dwg는 DXF의 `1001 ACAD` xdata를 DWG에 쓸 때 APPID 참조를 **항상 핸들 0x12**로 쓴다. ACAD APPID의 실제 핸들(u04 0x20, u01 0x26)과 상관없다. 근거는 `dwgread -O JSON`으로 본 STYLE의 eed handle `[5,1,18]`이다.
- 그래서 draft3에서 저장한 u01·u04·u07 DWG의 STYLE xdata는 모두 없는 객체(0x12)를 가리켰다. ODA는 이를 `1001 _APP12`로 읽는다.
  - draft3 보고서는 이 사실을 놓쳤다. `tools/oda.py`가 `x.dwg`와 `x.dxf`의 결과를 같은 이름 `x.oda.dxf`로 써서, DWG의 결과를 DXF의 결과가 덮어썼기 때문이다(draft4에서 `x.dwg.oda.dxf`로 고침).
- 수정: R2000 라이터가 ACAD APPID에 핸들 **12**를 준다. 원본에서 12가 이미 쓰이고 있으면 예전처럼 쓰고 경고를 남긴다. 그 결과 u04 DWG의 Standard·돋움·굴림이 ODA에서 `1001 ACAD`로 읽힌다(`work/gates/h0_u04_styles.txt`).
- 남은 것: ACAD가 아닌 앱의 xdata(u04에서 AVE_RENDER 등 266개)는 계속 끊긴다(ODA `_APP0`). 읽기 오류는 아니다.

## u04 한글 "???" — 원인 후보

- u04의 한글 TEXT(모형 516개 중 한글 38개)는 **Standard**(한글 31개)와 **돋움**(한글 7개)을 쓴다. MTEXT는 모형에 1개, 인라인 `\f`는 없다.
  - 둘 다 치환 대상이 맞았고(원본 `Gulim.ttf`·`Dotum.ttf`는 PC에 없음), draft3는 이를 B 형식(그룹 3 비움 + `Gulim`/`Dotum`)으로 바꿨다. 즉 치환 누락은 아니다.
- draft3 DWG에서는 그 xdata가 위 결함 때문에 끊겨 있었다. 그래서 nanoCAD 입장에서는 "글꼴 파일 없음 + 패밀리 없음"인 스타일이었다.
- 그런데 u01의 'PDF Arial'도 DWG에서 같은 상태(그룹 3 비움, xdata 끊김)였는데 nanoCAD에서 한글이 보였다(사용자 확인). 그래서 끊긴 xdata만으로는 설명이 끝나지 않는다. **UNVERIFIED**이고, 매트릭스 결과로 판정한다.
- draft4 기본(C)은 그룹 3에 실제 파일 `gulim.ttc`를 쓰므로 xdata가 끊겨도 글꼴 파일로 찾을 수 있다. xdata도 이제 ACAD에 붙는다.

## 사람 확인 뒤 바꾸는 방법 (한 줄)

`app/NexysDraft.Core/Dxf/FontCompat.cs`

```csharp
public const FontCompatMode DefaultMode = FontCompatMode.C;   // A~G 중 nanoCAD 5에서 한글이 보인 변형
```

글꼴 이름 대응표(`MapFamily`): 굴림·굴림체 → Gulim·gulim.ttc, 돋움·돋움체 → Dotum·gulim.ttc, 바탕·바탕체 → Batang·batang.ttc, 맑은 고딕 → Malgun Gothic·malgun.ttf, 그 외 → Gulim. 파일이 `%WINDIR%\Fonts`에 없으면 Malgun Gothic·malgun.ttf로 대신한다. 글꼴 관련 문자열(그룹 3·4·xdata 패밀리)은 모두 ASCII로 쓴다. 한글 패밀리 이름을 가진 스타일도 치환 대상에 넣는다.
