---
name: reference_ci_cd
description: GitHub Actions CI/CD workflow configuration for file-sync project
type: reference
---

# CI/CD 配置

## GitHub Actions 发布工作流

**文件位置**: `.github/workflows/release.yml`

**触发条件**:
1. Push 到 main 分支
2. 创建以 `v` 开头的 tag（`git tag v1.x.x && git push origin v1.x.x`）
3. 在 GitHub 网页上手动创建 Release

**工作流**:
1. Checkout code
2. Setup .NET 10
3. Restore dependencies
4. Build Release
5. Run tests
6. Publish self-contained single-file exe
7. Create zip archive
8. Create GitHub Release

**发布物**: `file-sync-{version}-win-x64.zip`
