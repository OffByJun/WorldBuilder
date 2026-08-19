# Installation

이 문서는 WorldBuilder 패키지를 프로젝트에 설치하고 사용할 수 있도록 준비하는 방법을 설명합니다.

---

# 요구 사항

WorldBuilder를 사용하기 전에 다음 환경을 권장합니다.

|항목|설명|
|---|---|
|Unity|package.json에 명시된 지원 버전 이상|
|Package Manager|Unity Package Manager 사용 가능|
|Git|Git URL 설치 시 필요|

> 권장: 최신 LTS 버전의 Unity Editor

---

# 설치 방법

WorldBuilder는 Unity Package Manager(UPM)를 통해 설치할 수 있습니다.

지원하는 설치 방식은 다음과 같습니다.

- Git URL
- Local Package
- Embedded Package

---

# Git URL 설치

1. Unity를 실행합니다.

2. **Window → Package Manager**를 엽니다.

3. 좌측 상단의 **+** 버튼을 클릭합니다.

4. **Add package from Git URL...** 을 선택합니다.

5. 저장소 URL을 입력합니다.

예시

```
https://github.com/USERNAME/WorldBuilder.git
```

또는

```
https://github.com/USERNAME/WorldBuilder.git#master
```

6. Install을 누릅니다.

---

# Local Package 설치

프로젝트를 직접 다운로드한 경우 사용할 수 있습니다.

1.

Package Manager를 엽니다.

2.

+ 버튼을 클릭합니다.

3.

**Add package from disk...**

4.

package.json을 선택합니다.

예시

```
WorldBuilder/

    package.json
```

5.

패키지가 프로젝트에 등록됩니다.

---

# Embedded Package

패키지를 직접 수정하려는 경우 권장됩니다.

프로젝트의

```
Packages/
```

폴더 안에 패키지를 복사합니다.

예시

```
Packages/

    com.emiteat.worldbuilder/
```

Unity는 Embedded Package를 자동으로 인식합니다.

---

# 설치 확인

설치가 완료되면

Package Manager에서

```
WorldBuilder
```

패키지가 표시됩니다.

또한 메뉴 또는 Editor Window에서 WorldBuilder를 열 수 있어야 합니다.

---

# 의존성

WorldBuilder는 package.json에 정의된 패키지에 의존합니다.

패키지 설치 시 Unity가 필요한 의존성을 자동으로 설치합니다.

만약 패키지가 설치되지 않는다면

- Unity 버전을 확인합니다.
- package.json을 확인합니다.
- Package Manager 오류 로그를 확인합니다.

---

# 프로젝트 최초 설정

프로젝트를 처음 열었다면 다음을 권장합니다.

- 모든 스크립트 컴파일 완료
- Package Import 완료
- Console 오류 확인
- WorldBuilder Window 실행

오류가 없는 상태에서 작업을 시작하는 것을 권장합니다.

---

# 업데이트

Git URL을 사용하는 경우

Package Manager에서

```
Update
```

버튼을 통해 최신 버전으로 업데이트할 수 있습니다.

Local Package는 최신 버전을 다시 다운로드하여 교체하면 됩니다.

---

# 문제 해결

## 패키지가 보이지 않습니다.

- package.json 위치를 확인합니다.
- Unity Console의 오류를 확인합니다.

---

## 컴파일 오류가 발생합니다.

- 지원하는 Unity 버전인지 확인합니다.
- 의존성 패키지가 모두 설치되었는지 확인합니다.

---

## Git URL 설치가 실패합니다.

다음을 확인하십시오.

- 저장소 URL
- 브랜치 이름
- 인터넷 연결
- Git 접근 권한

---

# 다음 문서

설치가 완료되었다면

다음으로 **QuickStart.md**를 읽는 것을 권장합니다.