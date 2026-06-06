## Summary

<!-- What does this change, and why? -->

## Checklist

- [ ] `./build.ps1` passes locally (restore, format-verify, build, test) -- 0 warnings, all tests green
- [ ] Protocol/byte-math changes are covered by a byte-for-byte encoder test
- [ ] Only the `LianLiPlugin` type is `public`; everything else stays `internal`
- [ ] Docs updated if behavior, interfaces, conventions, or the device protocol changed
