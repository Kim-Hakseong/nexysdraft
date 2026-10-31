# PDF 출력 구성 (draft5 I3)

PDF는 내장 작성기로 만든다(검색 가능한 실제 글자, 맑은 고딕 부분 글꼴). 기본으로 한 번에 파일 세 종류를 만든다.

| 파일 | 내용 | 쪽수 |
|---|---|---|
| `<이름>.pdf` | **합본**: 감지된 시트(도곽)마다 1쪽, 시트 밖 내용이 있으면 마지막에 "나머지" 1쪽. 시트가 없으면 전체 도면 1쪽 | 시트 수 (+1) |
| `<이름>__001.pdf`, `__002.pdf` … | **시트별 개별 파일**: 합본의 쪽마다 1개. 나머지 쪽은 `<이름>__rest.pdf` | 각 1쪽 |
| `<이름>__overview.pdf` | **개요**: 모형 공간 전체를 1장에 | 1쪽 |

- 용지는 쪽마다 자동으로 고른다. 도면의 플롯 설정이 가리키는 ISO 용지에 가장 가까운 것을 쓰고, 없으면 A3다. 가로·세로는 시트 비율을 따르고 여백은 10 mm다.
- 저장이 끝나면 상태줄과 명령줄에 만든 파일 목록이 나온다(`PDF: n file(s), m page(s) — 이름 (쪽, 용지), …`).

## 명령줄 (`--script`)

```
pdf C:\out\a.pdf                     세 종류 모두 (기본)
pdf C:\out\a.pdf --merged            합본만
pdf C:\out\a.pdf --sheets            시트별 개별 파일만
pdf C:\out\a.pdf --overview          개요만
pdf C:\out\a.pdf --merged --overview 골라서 조합
```

`result.json`의 `files`에 파일마다 `path`, `kind`(merged · sheet · overview), `pages`, `paper`가 들어간다.

## 관문 (work/gates/i3_pdf_set.txt)

- u05: 합본 10쪽(시트 9 + 나머지 1), 개별 10개, 개요 1개로 파일 12개다. 모든 파일에서 pdftotext로 글자가 추출된다.
- u01: 시트 1개로 파일 3개다.
