# Release channel audit, October 12, 2026

Stable releases should be built from a tested commit on `main`. Keep transcript
quality development on `feature/transcript-quality` until its quality and
installed-package gates pass. No channel was published or updated in this audit.

## Current main

Remote main is `9c4e48ca4c36a092e12a0b6fa1400978185f0f9d`. Its latest
[Windows CI run](https://github.com/divyangchauhan/Shruti/actions/runs/37352916270)
completed successfully. The workflow validates, tests, builds native code, and
produces an unsigned MSIX artifact. A push to main does not itself publish a
GitHub release or update Store, Scoop, or WinGet metadata.

## Channel findings

| Channel | Evidence checked | Decision |
| --- | --- | --- |
| Microsoft Store | Public listing returned HTTP 200 with the Shruti title. Partner Center opened to sign-in, so live submission/package version was not verified. | Do not submit this feature yet. Check the currently published package before deciding on a main-only update. |
| GitHub releases | The only release is draft `v1.0.2`, targeting main `9c4e48c`, with unsigned MSIX and SHA256 assets. No public release or git tag exists. | Keep the Store candidate private. A public standalone installer needs a trusted distribution package and install/upgrade/uninstall checks. |
| Scoop | This repo has no Shruti manifest or publication workflow. The owner's public repos contain `scoop-mushak`, but no Shruti bucket. The official main/extras `shruti.json` paths returned 404. This search does not cover every third-party bucket. | Start a Shruti bucket only after a public, versioned installer or portable artifact exists. Do not point it at the draft unsigned MSIX. |
| WinGet Store source | `winget search --id 9N39R62TTCPS --exact --source msstore` returned Shruti. Version was Unknown. | The Store source already exposes the app. Store updates use the existing listing. |
| WinGet community source | `winget search Shruti --source winget` found no package. No DivyangChauhan directory existed in the community manifest path. The owner's WinGet fork branches were for Mushak. | A separate community package needs a public versioned installer, hashes, correct identity, and validated installation behavior. |

The draft release description says that 1.0.2 has no application-code changes
from the Store build and only aligns main with the code already shipped. That
description is historical evidence, not a live Store version check. There is no
verified feature reason here to resubmit 1.0.2 just because main was merged.

## Distribution plan after validation

Merge reviewed changes into main, rerun CI, record the exact commit and version,
and build the channel artifacts from that commit. Send the Store identity MSIX
to Partner Center. Publish a separately installable, signed GitHub artifact with
checksums and release notes. Update Scoop and the community WinGet manifests to
that immutable public release, then check clean install, upgrade, and uninstall
through each channel. Avoid committing or publishing model weights with the app.

The new package flow includes the pinned CPU cleanup runtime. The optional
download fetches weights only. Include the runtime/model licenses, describe the
local cleanup and cursor-context behavior in Store metadata and privacy text,
and provide certification notes that let reviewers test it. Microsoft describes
dynamic code inclusion and privacy disclosures in its
[Store policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies).
This audit does not establish certification acceptance.

WinGet documents its separate package sources and manifest fields in
[the install command](https://learn.microsoft.com/en-us/windows/package-manager/winget/install)
and [manifest guidance](https://learn.microsoft.com/en-us/windows/package-manager/package/manifest).
The live Store listing is [Shruti](https://apps.microsoft.com/detail/9N39R62TTCPS).
